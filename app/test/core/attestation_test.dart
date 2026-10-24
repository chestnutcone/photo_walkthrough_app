import 'dart:async';
import 'dart:convert';

import 'package:crypto/crypto.dart';
import 'package:flutter/foundation.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:vela_app/core/api/api_providers.dart';
import 'package:vela_app/core/attestation/app_attest_client.dart';
import 'package:vela_app/core/attestation/attestation_client.dart';
import 'package:vela_app/core/attestation/platform_attestation.dart';
import 'package:vela_app/core/attestation/play_integrity_client.dart';
import 'package:vela_app/core/auth/session_store.dart';

import 'support/fakes.dart';

class _CountingClient implements AttestationClient {
  int calls = 0;
  final actions = <String>[];
  @override
  Future<String?> token(String action) async {
    actions.add(action);
    return 'tok-${calls++}';
  }
}

ProviderContainer _container(FakeAdapter adapter, AttestationClient client) {
  final c = ProviderContainer(
    overrides: [
      sessionStorageProvider.overrideWithValue(FakeSessionStorage()),
      httpClientAdapterProvider.overrideWithValue(adapter),
      attestationClientProvider.overrideWithValue(client),
    ],
  );
  addTearDown(c.dispose);
  return c;
}

FakeAdapter _ok() => FakeAdapter((_) => jsonResponse(200, okEnvelope({})));

void main() {
  test('attestation_header_added_only_on_protected_endpoints', () async {
    final client = _CountingClient();
    final adapter = _ok();
    final dio = _container(adapter, client).read(dioProvider);

    await dio.post<dynamic>('/v1/auth/email/request', data: {'email': 'a@b.c'});
    await dio.post<dynamic>('/v1/auth/sso', data: {});
    await dio.post<dynamic>('/v1/works', data: {});

    expect(client.actions, ['email_request', 'sso', 'create_work']);
    final headers = adapter.requests.map((r) => r.headers).toList();
    expect(headers.map((h) => h['X-Attestation']), ['tok-0', 'tok-1', 'tok-2']);
    expect(
      headers.map((h) => h['X-Platform']),
      everyElement(anyOf('android', 'ios')),
    );
  });

  test(
    'attestation_header_absent_on_other_endpoint_and_method_combos',
    () async {
      final client = _CountingClient();
      final adapter = _ok();
      final dio = _container(adapter, client).read(dioProvider);

      await dio.get<dynamic>('/v1/works');
      await dio.post<dynamic>('/v1/works/w1/publish', data: {});
      await dio.post<dynamic>('/v1/auth/email/verify', data: {});
      await dio.post<dynamic>('/v1/auth/refresh', data: {});
      await dio.put<dynamic>('/v1/works', data: {});
      await dio.get<dynamic>('/v1/auth/sso');
      await dio.post<dynamic>('/v1/works/', data: {});
      await dio.post<dynamic>('/v1/reports', data: {});

      expect(client.calls, 0);
      for (final r in adapter.requests) {
        expect(r.headers.containsKey('X-Attestation'), isFalse, reason: r.path);
      }
    },
  );

  test('null_token_omits_the_header_and_the_server_decides', () async {
    final adapter = _ok();
    final dio = _container(
      adapter,
      const NoAttestationClient(),
    ).read(dioProvider);

    await dio.post<dynamic>('/v1/works', data: {});

    expect(adapter.requests.single.headers.containsKey('X-Attestation'), false);
  });

  test('a_throwing_client_never_blocks_the_request', () async {
    final adapter = _ok();
    final dio = _container(adapter, _ThrowingClient()).read(dioProvider);

    final res = await dio.post<dynamic>('/v1/works', data: {});

    expect(res.statusCode, 200);
    expect(adapter.requests.single.headers.containsKey('X-Attestation'), false);
  });

  test('every_attempt_mints_a_fresh_token_never_cached', () async {
    final client = _CountingClient();
    final adapter = FakeAdapter((o) {
      return o.headers['X-Attestation'] == 'tok-1'
          ? jsonResponse(200, okEnvelope({}))
          : jsonResponse(403, errEnvelope('attestation_failed'));
    });
    final dio = _container(adapter, client).read(dioProvider);

    await expectLater(
      dio.post<dynamic>('/v1/auth/sso', data: {}),
      throwsA(anything),
    );
    await dio.post<dynamic>('/v1/auth/sso', data: {});

    expect(adapter.requests.map((r) => r.headers['X-Attestation']), [
      'tok-0',
      'tok-1',
    ]);
  });

  group('PlayIntegrityAttestationClient', () {
    test(
      'requests_a_standard_token_with_base64url_sha256_of_the_action',
      () async {
        final prepared = <int>[];
        final hashes = <String>[];
        final client = PlayIntegrityAttestationClient(
          cloudProjectNumber: 1234,
          prepare: (n) async => prepared.add(n),
          request: (hash) async {
            hashes.add(hash);
            return 'jwe';
          },
        );

        expect(await client.token('create_work'), 'jwe');
        expect(await client.token('sso'), 'jwe');

        final expected = base64Url
            .encode(sha256.convert(utf8.encode('create_work')).bytes)
            .replaceAll('=', '');
        expect(hashes.first, expected);
        expect(hashes.first, isNot(contains('=')));
        expect(prepared, [1234], reason: 'provider prepared once');
      },
    );

    test('returns_null_when_unconfigured_or_the_plugin_fails', () async {
      var called = false;
      final off = PlayIntegrityAttestationClient(
        cloudProjectNumber: 0,
        prepare: (_) async => called = true,
        request: (_) async => 'x',
      );
      expect(await off.token('sso'), isNull);
      expect(called, isFalse);

      final failing = PlayIntegrityAttestationClient(
        cloudProjectNumber: 5,
        prepare: (_) async {},
        request: (_) async => throw StateError('no play services'),
      );
      expect(await failing.token('sso'), isNull);
    });

    test('a_failed_prepare_is_retried_on_the_next_token', () async {
      var attempts = 0;
      final client = PlayIntegrityAttestationClient(
        cloudProjectNumber: 5,
        prepare: (_) async {
          if (attempts++ == 0) throw StateError('transient');
        },
        request: (_) async => 'jwe',
      );
      expect(await client.token('sso'), isNull);
      expect(await client.token('sso'), 'jwe');
    });
  });

  group('AppAttestClient', () {
    test('builds_challenge_dot_attestation_both_base64url', () async {
      final now = DateTime.fromMillisecondsSinceEpoch(
        1790000000 * 1000,
        isUtc: true,
      );
      String? seenChallenge;
      final raw = [250, 251, 252, 253, 254, 255, 1, 2];
      final client = AppAttestClient(
        enabled: true,
        clock: () => now,
        generateKey: () async => 'key1',
        attestKey: (keyId, challenge) async {
          seenChallenge = challenge;
          return base64.encode(raw);
        },
      );

      final token = await client.token('sso');

      expect(seenChallenge, 'sso|1790000000');
      final parts = token!.split('.');
      expect(parts, hasLength(2));
      expect(token, isNot(contains('=')));
      expect(token, isNot(contains('+')));
      expect(token, isNot(contains('/')));
      expect(
        utf8.decode(base64Url.decode(base64Url.normalize(parts[0]))),
        seenChallenge,
      );
      expect(base64Url.decode(base64Url.normalize(parts[1])), raw);
    });

    test('generates_a_new_key_for_every_token', () async {
      var keys = 0;
      final client = AppAttestClient(
        enabled: true,
        clock: DateTime.now,
        generateKey: () async => 'k${keys++}',
        attestKey: (id, c) async => base64.encode([1]),
      );
      await client.token('sso');
      await client.token('sso');
      expect(keys, 2);
    });

    test('null_when_disabled_or_failing', () async {
      final off = AppAttestClient(
        enabled: false,
        clock: DateTime.now,
        generateKey: () async => fail('must not run'),
        attestKey: (a, b) async => fail('must not run'),
      );
      expect(await off.token('sso'), isNull);
      final broken = AppAttestClient(
        enabled: true,
        clock: DateTime.now,
        generateKey: () async => throw StateError('not supported'),
        attestKey: (a, b) async => '',
      );
      expect(await broken.token('sso'), isNull);
    });
  });

  test('platform_factory_picks_per_platform_and_nothing_on_web', () {
    expect(
      createAttestationClientFor(TargetPlatform.android, isWeb: false),
      isA<PlayIntegrityAttestationClient>(),
    );
    expect(
      createAttestationClientFor(TargetPlatform.iOS, isWeb: false),
      isA<AppAttestClient>(),
    );
    expect(
      createAttestationClientFor(TargetPlatform.android, isWeb: true),
      isA<NoAttestationClient>(),
    );
    expect(
      createAttestationClientFor(TargetPlatform.linux, isWeb: false),
      isA<NoAttestationClient>(),
    );
  });

  group('fix round 1', () {
    test('play_integrity_times_out_to_null', () async {
      final client = PlayIntegrityAttestationClient(
        cloudProjectNumber: 5,
        timeout: const Duration(milliseconds: 30),
        prepare: (_) => Completer<void>().future,
        request: (_) => Completer<String>().future,
      );
      expect(await client.token('sso'), isNull);
    });

    test(
      'play_integrity_request_timeout_is_null_and_next_call_recovers',
      () async {
        var hang = true;
        final client = PlayIntegrityAttestationClient(
          cloudProjectNumber: 5,
          timeout: const Duration(milliseconds: 30),
          prepare: (_) async {},
          request: (_) =>
              hang ? Completer<String>().future : Future.value('jwe'),
        );
        expect(await client.token('sso'), isNull);
        hang = false;
        expect(await client.token('sso'), 'jwe');
      },
    );

    test('app_attest_times_out_to_null', () async {
      final hungKey = AppAttestClient(
        enabled: true,
        timeout: const Duration(milliseconds: 30),
        generateKey: () => Completer<String>().future,
        attestKey: (a, b) async => '',
      );
      expect(await hungKey.token('sso'), isNull);
      final hungAttest = AppAttestClient(
        enabled: true,
        timeout: const Duration(milliseconds: 30),
        generateKey: () async => 'k',
        attestKey: (a, b) => Completer<String>().future,
      );
      expect(await hungAttest.token('sso'), isNull);
    });

    test('a_retry_that_mints_null_removes_the_stale_header', () async {
      var calls = 0;
      final client = _SequenceClient(['used-token', null]);
      final session = Session(
        accessToken: 'A',
        refreshToken: 'R',
        accessExpiresAt: DateTime.utc(2030),
      ).encode();
      final adapter = FakeAdapter((o) {
        if (o.path == '/v1/auth/refresh') {
          return jsonResponse(200, tokenPair('A2', 'R2'));
        }
        calls++;
        return o.headers['Authorization'] == 'Bearer A2'
            ? jsonResponse(200, okEnvelope({}))
            : jsonResponse(401, errEnvelope('unauthorized'));
      });
      final c = ProviderContainer(
        overrides: [
          sessionStorageProvider.overrideWithValue(FakeSessionStorage(session)),
          httpClientAdapterProvider.overrideWithValue(adapter),
          attestationClientProvider.overrideWithValue(client),
        ],
      );
      addTearDown(c.dispose);

      await c.read(dioProvider).post<dynamic>('/v1/works', data: {});

      final works = adapter.requests
          .where((r) => r.path == '/v1/works')
          .toList();
      expect(calls, 2);
      expect(works.first.headers['X-Attestation'], 'used-token');
      expect(works.last.headers.containsKey('X-Attestation'), isFalse);
    });
  });
}

class _SequenceClient implements AttestationClient {
  _SequenceClient(this.values);
  final List<String?> values;
  @override
  Future<String?> token(String action) async =>
      values.isEmpty ? null : values.removeAt(0);
}

class _ThrowingClient implements AttestationClient {
  @override
  Future<String?> token(String action) async => throw StateError('boom');
}
