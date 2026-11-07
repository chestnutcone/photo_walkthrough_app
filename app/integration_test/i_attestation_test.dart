// Android attestation (owner decision 2026-11-03, flow N) against the second
// local API instance on :5081 that has `Abuse:RequireAttestation:Android=true`
// (scripts/run-e2e.sh starts it; Development uses the fake verifier, where the
// token "valid" passes). The app's API base URL is a compile-time define, so
// the test points the app's HTTP transport at :5081 instead.
import 'dart:typed_data';

import 'package:dio/dio.dart';
import 'package:dio/io.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:vela_app/core/attestation/attestation_client.dart';
import 'package:vela_app/core/attestation/play_integrity_client.dart';

import 'support/e2e.dart';

const attestedBase = 'http://10.0.2.2:5081';

/// Sends every app request to the attestation-required instance.
class _AttestedApiAdapter implements HttpClientAdapter {
  final _inner = IOHttpClientAdapter();

  @override
  Future<ResponseBody> fetch(
    RequestOptions options,
    Stream<Uint8List>? requestStream,
    Future<void>? cancelFuture,
  ) {
    if (options.baseUrl.startsWith(apiBase)) options.baseUrl = attestedBase;
    return _inner.fetch(options, requestStream, cancelFuture);
  }

  @override
  void close({bool force = false}) => _inner.close(force: force);
}

Future<void> openSignInSheet(WidgetTester tester) async {
  final pill = find.byWidgetPredicate(
    (w) =>
        w.key is ValueKey<String> &&
        (w.key! as ValueKey<String>).value.startsWith('follow-pill-'),
  );
  await pumpUntil(tester, pill);
  await tester.tap(pill.first);
  await pumpUntil(tester, find.byKey(const ValueKey('sso-google')));
}

Future<void> expectDeviceNotVerified(WidgetTester tester) async {
  await pumpUntil(tester, find.byKey(const ValueKey('sign-in-error')));
  expect(find.text("This device couldn't be verified."), findsOneWidget);
  // The app is still alive and the sheet usable.
  expect(find.byKey(const ValueKey('sso-google')), findsOneWidget);
}

void main() {
  initBinding();

  testWidgets(
    'N: the attestation-required instance refuses a request without a token',
    (tester) async {
      // Server side first, so a failure points at the API, not the app.
      await expectLater(
        http(
          'POST',
          '$attestedBase/v1/auth/sso',
          headers: {'X-Platform': 'android'},
          body: {
            'provider': 'fake',
            'idToken': 'fake:nx$uniq:nx$uniq@example.com',
            'nonce': 'n',
          },
        ),
        throwsA(
          predicate(
            (e) =>
                '$e'.contains('403') && '$e'.contains('attestation_required'),
          ),
        ),
      );
    },
  );

  testWidgets(
    'N: with a valid (fake Play Integrity) token, SSO sign-in works',
    (tester) async {
      final client = FakeAttestationClient('valid');
      await launchApp(
        tester,
        attestation: client,
        httpAdapter: _AttestedApiAdapter(),
      );
      await openSignInSheet(tester);
      await shot(tester, 'n-attestation', '01-sign-in-sheet');
      await tapKey(tester, 'sso-google');
      await pumpUntil(tester, find.byKey(const ValueKey('welcome-continue')));
      expect(client.actions, contains(AttestationActions.sso));
      await shot(tester, 'n-attestation', '02-signed-in-onboarding');
    },
  );

  testWidgets(
    'N: without a token the sign-in sheet says the device could not be verified',
    (tester) async {
      await launchApp(
        tester,
        attestation: const NoAttestationClient(),
        httpAdapter: _AttestedApiAdapter(),
      );
      await openSignInSheet(tester);
      await tapKey(tester, 'sso-google');
      await expectDeviceNotVerified(tester);
      await shot(tester, 'n-attestation', '03-sso-no-token');

      // Email code request is protected too.
      await tapKey(tester, 'sso-email');
      await enterKey(tester, 'email-field', 'nt$uniq@example.com');
      await tapKey(tester, 'email-send');
      await pumpUntil(tester, find.text("This device couldn't be verified."));
      await shot(tester, 'n-attestation', '04-email-no-token');
    },
  );

  testWidgets(
    'N: the real Play Integrity plugin failing on the emulator degrades to the same message',
    (tester) async {
      // A non-zero project number makes the client call the real plugin; the
      // emulator has no linked Cloud project, so it fails (or times out) and the
      // request goes without a token. No crash, the same error as above.
      await launchApp(
        tester,
        attestation: PlayIntegrityAttestationClient(
          cloudProjectNumber: 123456789012,
        ),
        httpAdapter: _AttestedApiAdapter(),
      );
      await openSignInSheet(tester);
      await tapKey(tester, 'sso-google');
      await expectDeviceNotVerified(tester);
      await shot(tester, 'n-attestation', '05-real-plugin-no-token');
    },
  );
}
