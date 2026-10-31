import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/painting.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:vela_app/core/api/api_providers.dart';
import 'package:vela_app/core/auth/auth_controller.dart';
import 'package:vela_app/core/auth/local_data.dart';
import 'package:vela_app/core/auth/session_store.dart';
import 'package:vela_app/features/auth/sign_out.dart';
import 'package:vela_app/features/auth/sso_client.dart';

import 'support/fakes.dart';

/// Final review I-1 / M-1 / M-2: every way a session ends (voluntary or not)
/// runs the same sign-out cleaners and drops in-memory caches;
/// account_deleted runs every cleaner.
void main() {
  final now = DateTime.utc(2026, 10, 31, 12);

  late List<String> ran;
  late int memoryClears;

  ProviderContainer make(FakeAdapter adapter, FakeSessionStorage storage) {
    ran = [];
    memoryClears = 0;
    final c = ProviderContainer(
      overrides: [
        sessionStorageProvider.overrideWithValue(storage),
        httpClientAdapterProvider.overrideWithValue(adapter),
        clockProvider.overrideWithValue(() => now),
        inMemoryCachesClearerProvider.overrideWithValue(() => memoryClears++),
      ],
    );
    addTearDown(c.dispose);
    final reg = c.read(localDataRegistryProvider);
    reg.register('device', () async => ran.add('device'));
    reg.register('scoped', () async => ran.add('scoped'), onSignOut: true);
    return c;
  }

  FakeSessionStorage stored() => FakeSessionStorage(
    Session(
      accessToken: 'A1',
      refreshToken: 'R1',
      accessExpiresAt: now.add(const Duration(minutes: 15)),
    ).encode(),
  );

  Future<void> callMe(ProviderContainer c) => expectLater(
    c.read(dioProvider).get<dynamic>('/v1/me'),
    throwsA(isA<DioException>()),
  );

  test('a rejected refresh token runs the sign-out cleaners', () async {
    final storage = stored();
    final c = make(
      FakeAdapter(
        (o) => jsonResponse(
          401,
          errEnvelope(
            o.path == '/v1/auth/refresh'
                ? 'invalid_refresh_token'
                : 'unauthorized',
          ),
        ),
      ),
      storage,
    );

    await callMe(c);

    expect(c.read(sessionProvider).value, isNull);
    expect(storage.value, isNull);
    expect(ran, ['scoped']);
    expect(memoryClears, 1);
  });

  test(
    'a retried request that still 401s runs the sign-out cleaners',
    () async {
      final c = make(
        FakeAdapter(
          (o) => o.path == '/v1/auth/refresh'
              ? jsonResponse(200, tokenPair('A2', 'R2'))
              : jsonResponse(401, errEnvelope('unauthorized')),
        ),
        stored(),
      );

      await callMe(c);

      expect(c.read(sessionProvider).value, isNull);
      expect(ran, ['scoped']);
      expect(memoryClears, 1);
    },
  );

  test('account_suspended runs the sign-out cleaners only', () async {
    final c = make(
      FakeAdapter((_) => jsonResponse(403, errEnvelope('account_suspended'))),
      stored(),
    );

    await callMe(c);

    expect(c.read(accountBlockProvider), AccountBlock.suspended);
    expect(ran, ['scoped']);
    expect(memoryClears, 1);
  });

  test('account_deleted runs every cleaner', () async {
    final c = make(
      FakeAdapter((_) => jsonResponse(403, errEnvelope('account_deleted'))),
      stored(),
    );

    await callMe(c);

    expect(c.read(accountBlockProvider), AccountBlock.deleted);
    expect(ran, ['device', 'scoped']);
    expect(memoryClears, 1);
  });

  test('account_deleted on refresh runs every cleaner', () async {
    final c = make(
      FakeAdapter(
        (o) => o.path == '/v1/auth/refresh'
            ? jsonResponse(403, errEnvelope('account_deleted'))
            : jsonResponse(401, errEnvelope('unauthorized')),
      ),
      stored(),
    );

    await callMe(c);

    expect(c.read(accountBlockProvider), AccountBlock.deleted);
    expect(ran, ['device', 'scoped']);
  });

  test('a transient refresh failure runs no cleaner', () async {
    final c = make(
      FakeAdapter(
        (o) => o.path == '/v1/auth/refresh'
            ? jsonResponse(503, errEnvelope('unavailable'))
            : jsonResponse(401, errEnvelope('unauthorized')),
      ),
      stored(),
    );

    await callMe(c);

    expect(c.read(sessionProvider).value, isNotNull);
    expect(ran, isEmpty);
    expect(memoryClears, 0);
  });

  test(
    'a 403 on refresh without an account code keeps the session (M-1)',
    () async {
      final storage = stored();
      final c = make(
        FakeAdapter(
          (o) => o.path == '/v1/auth/refresh'
              ? jsonResponse(403, errEnvelope('forbidden'))
              : jsonResponse(401, errEnvelope('unauthorized')),
        ),
        storage,
      );

      await callMe(c);

      expect(c.read(sessionProvider).value?.refreshToken, 'R1');
      expect(storage.value, isNotNull);
      expect(c.read(accountBlockProvider), isNull);
      expect(ran, isEmpty);
    },
  );

  test('a 403 on refresh with a non-JSON body keeps the session', () async {
    final c = make(
      FakeAdapter(
        (o) => o.path == '/v1/auth/refresh'
            ? ResponseBody.fromString('<html>blocked</html>', 403)
            : jsonResponse(401, errEnvelope('unauthorized')),
      ),
      stored(),
    );

    await callMe(c);

    expect(c.read(sessionProvider).value?.refreshToken, 'R1');
  });

  test('voluntary sign-out runs the cleaners once and drops caches', () async {
    final c = make(
      FakeAdapter((_) => jsonResponse(200, okEnvelope(null))),
      stored(),
    );
    await c.read(sessionProvider.future);

    await c.read(signOutProvider)();

    expect(ran, ['scoped']);
    expect(memoryClears, 1);
  });

  test('account deletion runs every cleaner once and drops caches', () async {
    final c = make(
      FakeAdapter((_) => jsonResponse(200, okEnvelope(null))),
      stored(),
    );
    await c.read(sessionProvider.future);

    await c.read(wipeLocalDataProvider)();

    expect(ran, ['device', 'scoped']);
    expect(memoryClears, 1);
    expect(c.read(sessionProvider).value, isNull);
  });

  testWidgets('the default in-memory clearer empties the image cache', (
    tester,
  ) async {
    final cache = PaintingBinding.instance.imageCache;
    final completer = OneFrameImageStreamCompleter(
      Completer<ImageInfo>().future,
    );
    cache.putIfAbsent('private-work-image', () => completer);
    expect(cache.containsKey('private-work-image'), isTrue);

    final c = ProviderContainer();
    addTearDown(c.dispose);
    c.read(inMemoryCachesClearerProvider)();

    expect(cache.containsKey('private-work-image'), isFalse);
  });

  test('the real SSO client registers the SDK sign-out for sign-out (M-2)', () {
    final c = ProviderContainer();
    addTearDown(c.dispose);
    final reg = c.read(localDataRegistryProvider);
    expect(reg.signOutCleaners, isEmpty);

    c.read(ssoClientProvider);

    expect(reg.signOutCleaners, hasLength(1));
  });
}
