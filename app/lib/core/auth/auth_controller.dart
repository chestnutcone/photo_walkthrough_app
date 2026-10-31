import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/painting.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:vela_api_client/vela_api_client.dart';

import '../api/api_providers.dart';
import 'local_data.dart';
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

/// Increments on every sign-in and sign-out, but not on a token refresh.
/// Per-account caches key on this: the session has no user id, and a
/// refreshed session object must not look like a different account.
final sessionEpochProvider = NotifierProvider<SessionEpoch, int>(
  SessionEpoch.new,
);

class SessionEpoch extends Notifier<int> {
  @override
  int build() => 0;

  void bump() => state++;
}

/// Drops in-memory caches that may hold the signed-in account's data
/// (decoded private, pending and blocked work images, avatars). Runs on every
/// sign-out, voluntary or not. Tests override it.
final inMemoryCachesClearerProvider = Provider<void Function()>(
  (_) => clearImageCache,
);

/// Empties Flutter's decoded-image cache, including live images. A no-op
/// before the binding exists (plain unit tests).
void clearImageCache() {
  try {
    PaintingBinding.instance.imageCache
      ..clear()
      ..clearLiveImages();
  } on Object {
    // No binding yet: nothing has been decoded.
  }
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
    ref.read(sessionEpochProvider.notifier).bump();
    state = AsyncData(session);
    await _bestEffort(() => _storage.write(session.encode()));
  }

  /// Ends the session. Every sign-out goes through here, voluntary or not
  /// (sign-out button, rejected refresh token, a retried 401, a blocked
  /// account), so the device is left in the same state either way: the
  /// cleaners registered `onSignOut` run (all of them when [wipeAll], for a
  /// deleted account), in-memory caches are dropped, the epoch is bumped and
  /// the stored tokens are deleted. A failing cleaner never stops the clear.
  Future<void> clear({bool wipeAll = false}) async {
    final registry = ref.read(localDataRegistryProvider);
    await LocalDataRegistry.runAll(
      wipeAll ? registry.cleaners : registry.signOutCleaners,
    );
    try {
      ref.read(inMemoryCachesClearerProvider)();
    } on Object {
      // Best effort, like the cleaners.
    }
    ref.read(sessionEpochProvider.notifier).bump();
    state = const AsyncData(null);
    await _bestEffort(_storage.delete);
  }

  /// Clears the session and records why, for the suspended-account screen.
  /// A deleted account erases all local data, as deleting it here would.
  Future<void> blockAccount(AccountBlock reason) async {
    await clear(wipeAll: reason == AccountBlock.deleted);
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

  /// 401 (invalid_refresh_token) or a 403 that names the account
  /// (account_suspended / account_deleted). Any other 403, for example from
  /// a WAF or a future rate/attestation rule, keeps the session.
  static bool _isDefinitiveRejection(DioException e) {
    final status = e.response?.statusCode;
    return status == 401 || accountBlockFor(e) != null;
  }

  Session _fromPair(String access, String refresh, int expiresIn) => Session(
    accessToken: access,
    refreshToken: refresh,
    accessExpiresAt: ref
        .read(clockProvider)()
        .add(Duration(seconds: expiresIn)),
  );
}
