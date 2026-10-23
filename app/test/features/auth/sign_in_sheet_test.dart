import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:vela_app/core/auth/auth_controller.dart';
import 'package:vela_app/core/router.dart';
import 'package:vela_app/features/auth/sso_client.dart';

import 'support/auth_harness.dart';

/// Keeps the onboarding flag (and so `/v1/me`) alive, as the router does.
ProviderContainer _container(WidgetTester tester) {
  final c = ProviderScope.containerOf(tester.element(find.byType(Scaffold)));
  c.listen(needsOnboardingProvider, (_, _) {});
  return c;
}

void main() {
  group('sso buttons', () {
    testWidgets('google_button_calls_sso_client_then_api', (tester) async {
      final h = await pumpSignIn(tester);

      await tester.tap(key('sso-google'));
      await tester.pumpAndSettle();

      expect(h.sso.calls, hasLength(1));
      expect(h.sso.calls.single.provider, 'google');
      final body = h.api.bodies('/v1/auth/sso').single;
      expect(body['provider'], 'google');
      expect(body['idToken'], 'idtoken-google');
      expect(body['nonce'], h.sso.calls.single.nonce);
      expect(h.stored, isTrue);
      expect(h.result, isTrue);
      expect(find.byKey(const ValueKey('sheet-close')), findsNothing);
    });

    testWidgets('each attempt uses a fresh random nonce', (tester) async {
      final sso = FakeSso()..failWith = const SsoCancelledException();
      final h = await pumpSignIn(tester, sso: sso);

      await tester.tap(key('sso-google'));
      await tester.pumpAndSettle();
      await tester.tap(key('sso-google'));
      await tester.pumpAndSettle();

      final nonces = h.sso.calls.map((c) => c.nonce).toList();
      expect(nonces, hasLength(2));
      expect(nonces[0], isNot(nonces[1]));
      expect(nonces[0].length, greaterThanOrEqualTo(32));
      expect(RegExp(r'^[A-Za-z0-9_-]+$').hasMatch(nonces[0]), isTrue);
    });

    testWidgets('apple_button_only_when_available', (tester) async {
      await pumpSignIn(tester, apple: false);
      expect(key('sso-apple'), findsNothing);
      expect(key('sso-google'), findsOneWidget);
      expect(key('sso-email'), findsOneWidget);
    });

    testWidgets('apple_button_calls_sso_client_with_apple', (tester) async {
      final h = await pumpSignIn(tester, apple: true);
      await tester.tap(key('sso-apple'));
      await tester.pumpAndSettle();
      expect(h.sso.calls.single.provider, 'apple');
      expect(h.api.bodies('/v1/auth/sso').single['provider'], 'apple');
      expect(h.result, isTrue);
    });

    testWidgets('cancelled_sso_shows_no_error_and_keeps_sheet', (tester) async {
      final sso = FakeSso()..failWith = const SsoCancelledException();
      final h = await pumpSignIn(tester, sso: sso);
      await tester.tap(key('sso-google'));
      await tester.pumpAndSettle();
      expect(h.api.all.where((r) => r.path == '/v1/auth/sso'), isEmpty);
      expect(key('sign-in-error'), findsNothing);
      expect(key('sso-google'), findsOneWidget);
      expect(h.stored, isFalse);
    });

    testWidgets('server_rejection_shows_error_and_stores_nothing', (
      tester,
    ) async {
      final h = await pumpSignIn(tester, api: AuthApi(ssoStatus: 401));
      await tester.tap(key('sso-google'));
      await tester.pumpAndSettle();
      expect(key('sign-in-error'), findsOneWidget);
      expect(h.stored, isFalse);
      expect(h.result, isNull);
    });

    testWidgets('double_tap_runs_one_attempt', (tester) async {
      final sso = FakeSso()..gate = Completer<String>();
      final h = await pumpSignIn(tester, sso: sso);
      await tester.tap(key('sso-google'));
      await tester.pump();
      await tester.tap(key('sso-google'), warnIfMissed: false);
      await tester.pump();
      expect(h.sso.calls, hasLength(1));
      sso.gate!.complete('tok');
      await tester.pumpAndSettle();
      expect(h.api.bodies('/v1/auth/sso'), hasLength(1));
    });

    testWidgets('sheet_cannot_be_dismissed_while_signing_in', (tester) async {
      final sso = FakeSso()..gate = Completer<String>();
      final h = await pumpSignIn(tester, sso: sso);
      await tester.tap(key('sso-google'));
      await tester.pump();
      await tester.tapAt(const Offset(195, 20)); // scrim
      await tester.pump(const Duration(milliseconds: 400));
      expect(key('sso-google'), findsOneWidget);
      sso.gate!.complete('tok');
      await tester.pumpAndSettle();
      expect(h.result, isTrue);
    });

    testWidgets('cancel_returns_false_from_sign_in_sheet', (tester) async {
      final h = await pumpSignIn(tester);
      await tester.tap(key('sheet-close'));
      await tester.pumpAndSettle();
      expect(h.result, isFalse);
      expect(h.stored, isFalse);
    });

    testWidgets('scrim_dismiss_returns_false', (tester) async {
      final h = await pumpSignIn(tester);
      await tester.tapAt(const Offset(195, 20));
      await tester.pumpAndSettle();
      expect(h.result, isFalse);
    });

    testWidgets('sheet_states_each_method_is_its_own_account', (tester) async {
      await pumpSignIn(tester);
      expect(find.textContaining('own account'), findsOneWidget);
    });
  });

  group('onboarding hand-off', () {
    testWidgets('new_user_sign_in_flags_needs_onboarding', (tester) async {
      final h = await pumpSignIn(
        tester,
        api: AuthApi(isNewUser: true, meOnboarded: false),
      );
      final c = _container(tester);
      await tester.tap(key('sso-google'));
      await tester.pumpAndSettle();
      expect(c.read(needsOnboardingProvider), isTrue);
      expect(h.result, isTrue);
    });

    testWidgets('returning_user_does_not_need_onboarding', (tester) async {
      await pumpSignIn(tester);
      final c = _container(tester);
      await tester.tap(key('sso-google'));
      await tester.pumpAndSettle();
      expect(c.read(needsOnboardingProvider), isFalse);
    });

    testWidgets('hint_covers_a_failed_me_lookup', (tester) async {
      final h = await pumpSignIn(
        tester,
        api: AuthApi(isNewUser: true, meStatus: 500),
      );
      final c = _container(tester);
      await tester.tap(key('sso-google'));
      await tester.pumpAndSettle();
      expect(c.read(needsOnboardingProvider), isTrue);
      expect(h.result, isTrue);
    });

    testWidgets('sign_out_clears_the_hint', (tester) async {
      await pumpSignIn(tester, api: AuthApi(isNewUser: true, meOnboarded: false));
      final c = _container(tester);
      await tester.tap(key('sso-google'));
      await tester.pumpAndSettle();
      expect(c.read(needsOnboardingProvider), isTrue);
      await c.read(sessionProvider.notifier).clear();
      await tester.pumpAndSettle();
      expect(c.read(needsOnboardingProvider), isFalse);
    });
  });
}
