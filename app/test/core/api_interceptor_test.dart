import 'dart:async';
import 'dart:convert';

import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:vela_app/core/api/api_providers.dart';
import 'package:vela_app/core/auth/auth_controller.dart';
import 'package:vela_app/core/auth/session_store.dart';

import 'support/fakes.dart';

final _now = DateTime.utc(2026, 10, 22, 12);

Session _session(String access, String refresh) => Session(
  accessToken: access,
  refreshToken: refresh,
  accessExpiresAt: _now.add(const Duration(minutes: 15)),
);

ProviderContainer _container(FakeAdapter adapter, FakeSessionStorage storage) {
  final c = ProviderContainer(
    overrides: [
      sessionStorageProvider.overrideWithValue(storage),
      httpClientAdapterProvider.overrideWithValue(adapter),
      clockProvider.overrideWithValue(() => _now),
    ],
  );
  addTearDown(c.dispose);
  return c;
}

FakeSessionStorage _stored(String access, String refresh) =>
    FakeSessionStorage(_session(access, refresh).encode());

void main() {
  test('interceptor_attaches_bearer_token', () async {
    final adapter = FakeAdapter((_) => jsonResponse(200, okEnvelope({})));
    final c = _container(adapter, _stored('A1', 'R1'));

    await c.read(dioProvider).get<dynamic>('/v1/me');

    expect(adapter.requests.single.headers['Authorization'], 'Bearer A1');
    expect(
      adapter.requests.single.headers['X-Platform'],
      anyOf('android', 'ios'),
    );
  });

  test('guest requests carry no Authorization header', () async {
    final adapter = FakeAdapter((_) => jsonResponse(200, okEnvelope({})));
    final c = _container(adapter, FakeSessionStorage());

    await c.read(dioProvider).get<dynamic>('/v1/works');

    expect(
      adapter.requests.single.headers.containsKey('Authorization'),
      isFalse,
    );
  });

  test('on_401_refreshes_once_and_retries', () async {
    final storage = _stored('A1', 'R1');
    final adapter = FakeAdapter((o) {
      if (o.path == '/v1/auth/refresh') {
        return jsonResponse(200, tokenPair('A2', 'R2'));
      }
      return o.headers['Authorization'] == 'Bearer A2'
          ? jsonResponse(200, okEnvelope({'ok': true}))
          : jsonResponse(401, errEnvelope('unauthorized'));
    });
    final c = _container(adapter, storage);

    final res = await c.read(dioProvider).get<dynamic>('/v1/me');

    expect(res.statusCode, 200);
    expect(adapter.count('/v1/auth/refresh'), 1);
    expect(adapter.count('/v1/me'), 2);
    expect(
      jsonDecode(
        adapter.requests.firstWhere((r) => r.path.endsWith('refresh')).data
            as String,
      ),
      {'refreshToken': 'R1'},
    );
    final saved = Session.decode(storage.value!);
    expect(saved.accessToken, 'A2');
    expect(saved.refreshToken, 'R2');
    expect(saved.accessExpiresAt, _now.add(const Duration(seconds: 900)));
  });

  test(
    'refresh request carries no bearer header and does not recurse',
    () async {
      final adapter = FakeAdapter((o) => jsonResponse(401, errEnvelope('x')));
      final c = _container(adapter, _stored('A1', 'R1'));

      await expectLater(
        c.read(dioProvider).get<dynamic>('/v1/me'),
        throwsA(isA<DioException>()),
      );

      final refresh = adapter.requests.firstWhere(
        (r) => r.path == '/v1/auth/refresh',
      );
      expect(refresh.headers.containsKey('Authorization'), isFalse);
      expect(adapter.count('/v1/auth/refresh'), 1);
    },
  );

  test('refresh_failure_clears_session', () async {
    final storage = _stored('A1', 'R1');
    final adapter = FakeAdapter((o) => jsonResponse(401, errEnvelope('x')));
    final c = _container(adapter, storage);

    await expectLater(
      c.read(dioProvider).get<dynamic>('/v1/me'),
      throwsA(isA<DioException>()),
    );

    expect(c.read(sessionProvider).value, isNull);
    expect(storage.value, isNull);
  });

  test(
    'retry that still 401s signs the user out without a second refresh',
    () async {
      final storage = _stored('A1', 'R1');
      final adapter = FakeAdapter(
        (o) => o.path == '/v1/auth/refresh'
            ? jsonResponse(200, tokenPair('A2', 'R2'))
            : jsonResponse(401, errEnvelope('unauthorized')),
      );
      final c = _container(adapter, storage);

      await expectLater(
        c.read(dioProvider).get<dynamic>('/v1/me'),
        throwsA(isA<DioException>()),
      );

      expect(adapter.count('/v1/auth/refresh'), 1);
      expect(c.read(sessionProvider).value, isNull);
    },
  );

  test('transient refresh failure keeps the session', () async {
    final storage = _stored('A1', 'R1');
    final adapter = FakeAdapter(
      (o) => o.path == '/v1/auth/refresh'
          ? jsonResponse(503, errEnvelope('unavailable'))
          : jsonResponse(401, errEnvelope('unauthorized')),
    );
    final c = _container(adapter, storage);

    await expectLater(
      c.read(dioProvider).get<dynamic>('/v1/me'),
      throwsA(isA<DioException>()),
    );

    expect(c.read(sessionProvider).value?.refreshToken, 'R1');
    expect(storage.value, isNotNull);
  });

  test('connection error during refresh keeps the session', () async {
    final adapter = FakeAdapter((o) {
      if (o.path == '/v1/auth/refresh') {
        throw DioException.connectionError(
          requestOptions: o,
          reason: 'offline',
        );
      }
      return jsonResponse(401, errEnvelope('unauthorized'));
    });
    final c = _container(adapter, _stored('A1', 'R1'));

    await expectLater(
      c.read(dioProvider).get<dynamic>('/v1/me'),
      throwsA(isA<DioException>()),
    );

    expect(c.read(sessionProvider).value?.refreshToken, 'R1');
  });

  test('concurrent_401s_trigger_a_single_refresh', () async {
    final gate = Completer<void>();
    final adapter = FakeAdapter((o) async {
      if (o.path == '/v1/auth/refresh') {
        await gate.future;
        return jsonResponse(200, tokenPair('A2', 'R2'));
      }
      return o.headers['Authorization'] == 'Bearer A2'
          ? jsonResponse(200, okEnvelope({}))
          : jsonResponse(401, errEnvelope('unauthorized'));
    });
    final c = _container(adapter, _stored('A1', 'R1'));
    final dio = c.read(dioProvider);

    final calls = [for (var i = 0; i < 4; i++) dio.get<dynamic>('/v1/me?i=$i')];
    await Future<void>.delayed(const Duration(milliseconds: 20));
    gate.complete();
    final results = await Future.wait(calls);

    expect(results.map((r) => r.statusCode), everyElement(200));
    expect(adapter.count('/v1/auth/refresh'), 1);
  });

  test(
    'a late 401 for an already rotated token retries without refreshing',
    () async {
      final adapter = FakeAdapter((o) async {
        if (o.path == '/v1/auth/refresh') {
          return jsonResponse(200, tokenPair('A2', 'R2'));
        }
        if (o.path == '/v1/slow') {
          await Future<void>.delayed(const Duration(milliseconds: 50));
        }
        return o.headers['Authorization'] == 'Bearer A2'
            ? jsonResponse(200, okEnvelope({}))
            : jsonResponse(401, errEnvelope('unauthorized'));
      });
      final c = _container(adapter, _stored('A1', 'R1'));
      final dio = c.read(dioProvider);

      final slow = dio.get<dynamic>('/v1/slow');
      await dio.get<dynamic>('/v1/me');
      final res = await slow;

      expect(res.statusCode, 200);
      expect(adapter.count('/v1/auth/refresh'), 1);
    },
  );

  for (final code in ['account_suspended', 'account_deleted']) {
    test('403 $code clears the session and flags the account', () async {
      final storage = _stored('A1', 'R1');
      final adapter = FakeAdapter((_) => jsonResponse(403, errEnvelope(code)));
      final c = _container(adapter, storage);

      await expectLater(
        c.read(dioProvider).get<dynamic>('/v1/me'),
        throwsA(isA<DioException>()),
      );

      expect(c.read(sessionProvider).value, isNull);
      expect(storage.value, isNull);
      expect(
        c.read(accountBlockProvider),
        code == 'account_suspended'
            ? AccountBlock.suspended
            : AccountBlock.deleted,
      );
    });
  }

  test('an ordinary 403 does not sign out', () async {
    final adapter = FakeAdapter(
      (_) => jsonResponse(403, errEnvelope('forbidden')),
    );
    final c = _container(adapter, _stored('A1', 'R1'));

    await expectLater(
      c.read(dioProvider).get<dynamic>('/v1/me'),
      throwsA(isA<DioException>()),
    );

    expect(c.read(sessionProvider).value, isNotNull);
    expect(c.read(accountBlockProvider), isNull);
  });

  test('signing in again resets the account block flag', () async {
    final adapter = FakeAdapter(
      (_) => jsonResponse(403, errEnvelope('account_suspended')),
    );
    final c = _container(adapter, _stored('A1', 'R1'));
    await expectLater(
      c.read(dioProvider).get<dynamic>('/v1/me'),
      throwsA(isA<DioException>()),
    );

    await c
        .read(sessionProvider.notifier)
        .signIn(accessToken: 'A9', refreshToken: 'R9', expiresIn: 900);

    expect(c.read(accountBlockProvider), isNull);
    expect(c.read(sessionProvider).value?.accessToken, 'A9');
  });
}
