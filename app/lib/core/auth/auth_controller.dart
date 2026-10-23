import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:vela_api_client/vela_api_client.dart';

import '../api/api_providers.dart';
import 'session_store.dart';

/// Why the server ended the account. B14 maps this to SuspendedScreen.
enum AccountBlock { suspended, deleted }

class AccountBlockNotifier extends Notifier<AccountBlock?> {
  @override
  AccountBlock? build() => null;

  void set(AccountBlock? value) => state = value;
}

final accountBlockProvider =
    NotifierProvider<AccountBlockNotifier, AccountBlock?>(
      AccountBlockNotifier.new,
    );

/// Reads the 403 envelope code of [e], if it ends the account.
AccountBlock? accountBlockFor(DioException e) {
  if (e.response?.statusCode != 403) return null;
  final body = e.response?.data;
  final code = body is Map && body['error'] is Map
      ? (body['error'] as Map)['code']
      : null;
  return switch (code) {
    'account_suspended' => AccountBlock.suspended,
    'account_deleted' => AccountBlock.deleted,
    _ => null,
  };
}

/// The refresh token is no longer usable; the session has been cleared.
class SessionExpiredException implements Exception {
  const SessionExpiredException();
  @override
  String toString() => 'SessionExpiredException';
}

final clockProvider = Provider<DateTime Function()>((_) => DateTime.now);

final sessionProvider = AsyncNotifierProvider<SessionNotifier, Session?>(
  SessionNotifier.new,
);

class SessionNotifier extends AsyncNotifier<Session?> {
  Future<Session>? _inFlightRefresh;

  SessionStorage get _storage => ref.read(sessionStorageProvider);

  @override
  Future<Session?> build() async {
    try {
      final raw = await _storage.read();
      if (raw == null) return null;
      try {
        return Session.decode(raw);
      } on Object {
        await _bestEffort(_storage.delete);
        return null;
      }
    } on Object {
      // Unreadable keystore (corruption, backup restore): treat as signed out.
      await _bestEffort(_storage.delete);
      return null;
    }
  }

  /// Storage failures must never break auth state; the in-memory session
  /// stays authoritative for the running app.
  static Future<void> _bestEffort(Future<void> Function() op) async {
    try {
      await op();
    } on Object {
      // ignored on purpose
    }
  }

  Future<Session?> current() => future;

  Future<void> signIn({
    required String accessToken,
    required String refreshToken,
    required int expiresIn,
  }) async {
    final session = _fromPair(accessToken, refreshToken, expiresIn);
    ref.read(accountBlockProvider.notifier).set(null);
    state = AsyncData(session);
    await _bestEffort(() => _storage.write(session.encode()));
  }

  Future<void> clear() async {
    state = const AsyncData(null);
    await _bestEffort(_storage.delete);
  }

  /// Clears the session and records why, for the suspended-account screen.
  Future<void> blockAccount(AccountBlock reason) async {
    await clear();
    ref.read(accountBlockProvider.notifier).set(reason);
  }

  /// Returns a usable session after a 401 on a request that was sent with
  /// [sentAccessToken]. All concurrent callers share one refresh call, and a
  /// caller whose token was already rotated just gets the current session.
  /// Throws [SessionExpiredException] (session cleared) when the server
  /// rejects the refresh token, or the transport [DioException] (session kept)
  /// when the failure may be transient.
  Future<Session> sessionAfter401(String sentAccessToken) async {
    final session = await future;
    if (session == null) throw const SessionExpiredException();
    if (session.accessToken != sentAccessToken) return session;
    return _inFlightRefresh ??= _refresh(session)
        .whenComplete(() => _inFlightRefresh = null);
  }

  Future<Session> _refresh(Session stale) async {
    try {
      final res = await AuthApi(ref.read(refreshDioProvider)).refresh(
        refreshRequest: RefreshRequest(refreshToken: stale.refreshToken),
      );
      final pair = res.data?.data;
      if (pair == null) throw const SessionExpiredException();
      final fresh = _fromPair(
        pair.accessToken,
        pair.refreshToken,
        pair.expiresIn,
      );
      // A sign-in or sign-out during the refresh wins.
      if (state.value?.refreshToken != stale.refreshToken) {
        throw const SessionExpiredException();
      }
      // Persist before anyone retries: the old refresh token is now dead.
      // If the write fails the in-memory session still works this run.
      state = AsyncData(fresh);
      await _bestEffort(() => _storage.write(fresh.encode()));
      return fresh;
    } on DioException catch (e) {
      if (_isDefinitiveRejection(e)) {
        final block = accountBlockFor(e);
        if (block != null) {
          await blockAccount(block);
        } else {
          await clear();
        }
        throw const SessionExpiredException();
      }
      rethrow;
    } on SessionExpiredException {
      if (state.value?.refreshToken == stale.refreshToken) await clear();
      rethrow;
    }
  }

  static bool _isDefinitiveRejection(DioException e) {
    final status = e.response?.statusCode;
    return status == 401 || status == 403;
  }

  Session _fromPair(String access, String refresh, int expiresIn) => Session(
    accessToken: access,
    refreshToken: refresh,
    accessExpiresAt: ref
        .read(clockProvider)()
        .add(Duration(seconds: expiresIn)),
  );
}
