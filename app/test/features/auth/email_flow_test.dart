import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'support/auth_harness.dart';

Future<AuthHarness> _toCodeStep(
  WidgetTester tester, {
  AuthApi? api,
  String email = '  Ana@Example.com ',
}) async {
  final h = await pumpSignIn(tester, api: api);
  await tester.tap(key('sso-email'));
  await tester.pumpAndSettle();
  await tester.enterText(key('email-field'), email);
  await tester.pump();
  await tester.tap(key('email-send'));
  await tester.pumpAndSettle();
  return h;
}

Future<void> _enter(WidgetTester tester, String code) async {
  await tester.enterText(key('code-field'), code);
  await tester.pumpAndSettle();
}

void main() {
  testWidgets('code_field_keeps_focus_after_a_wrong_code', (tester) async {
    await _toCodeStep(tester, api: AuthApi(verifyResults: [400]));
    await _enter(tester, '111111');
    final field = tester.widget<TextField>(key('code-field'));
    expect(field.focusNode!.hasFocus, isTrue);
    expect(field.enabled, isNot(false));
  });

  testWidgets('code_field_is_read_only_not_disabled_while_checking', (
    tester,
  ) async {
    final h = await _toCodeStep(tester);
    // Hold the verify call open by not answering: use a long-lived future.
    await tester.enterText(key('code-field'), '123456');
    await tester.pump();
    final field = tester.widget<TextField>(key('code-field'));
    expect(field.enabled, isNot(false));
    expect(field.readOnly, isTrue);
    await tester.pumpAndSettle();
    expect(h.api.verifyCount, 1);
  });

  testWidgets('email_flow_request_then_six_digit_entry_then_session', (
    tester,
  ) async {
    final h = await _toCodeStep(tester);
    expect(h.api.bodies('/v1/auth/email/request').single, {
      'email': 'Ana@Example.com',
    });
    expect(key('code-field'), findsOneWidget);

    await _enter(tester, '123456');

    expect(h.api.bodies('/v1/auth/email/verify').single, {
      'email': 'Ana@Example.com',
      'code': '123456',
    });
    expect(h.stored, isTrue);
    expect(h.result, isTrue);
  });

  testWidgets('send_is_disabled_until_the_email_looks_valid', (tester) async {
    await pumpSignIn(tester);
    await tester.tap(key('sso-email'));
    await tester.pumpAndSettle();
    FilledButton send() => tester.widget(key('email-send'));
    expect(send().onPressed, isNull);
    await tester.enterText(key('email-field'), 'ana@');
    await tester.pump();
    expect(send().onPressed, isNull);
    await tester.enterText(key('email-field'), 'ana@example.com');
    await tester.pump();
    expect(send().onPressed, isNotNull);
  });

  testWidgets('code_field_hints_one_time_code_autofill', (tester) async {
    await _toCodeStep(tester);
    final field = tester.widget<TextField>(key('code-field'));
    expect(field.autofillHints, contains(AutofillHints.oneTimeCode));
    expect(field.keyboardType, TextInputType.number);
  });

  testWidgets('pasted_code_with_spaces_submits_six_digits', (tester) async {
    final h = await _toCodeStep(tester);
    await _enter(tester, '123 456');
    expect(h.api.bodies('/v1/auth/email/verify').single['code'], '123456');
  });

  testWidgets('five_digits_do_not_submit', (tester) async {
    final h = await _toCodeStep(tester);
    await _enter(tester, '12345');
    expect(h.api.verifyCount, 0);
  });

  testWidgets('wrong_code_shows_error_and_sixth_attempt_message', (
    tester,
  ) async {
    final h = await _toCodeStep(tester, api: AuthApi(verifyResults: [400]));

    await _enter(tester, '111111');
    expect(find.textContaining('4 attempts left'), findsOneWidget);
    expect(tester.widget<TextField>(key('code-field')).controller!.text, '');

    for (var i = 0; i < 3; i++) {
      await _enter(tester, '222222');
    }
    expect(find.textContaining('1 attempt left'), findsOneWidget);

    await _enter(tester, '333333');
    expect(h.api.verifyCount, 5);
    expect(find.textContaining('Too many wrong codes'), findsOneWidget);
    // The sixth attempt is not even sent; the user must request a new code.
    expect(key('code-field'), findsNothing);
    expect(key('code-new'), findsOneWidget);
    expect(h.api.verifyCount, 5);
    expect(h.stored, isFalse);
  });

  testWidgets('new_code_after_exhaustion_resets_attempts', (tester) async {
    final h = await _toCodeStep(tester, api: AuthApi(verifyResults: [400]));
    for (var i = 0; i < 5; i++) {
      await _enter(tester, '111111');
    }
    await tester.tap(key('code-new'));
    await tester.pumpAndSettle();
    expect(h.api.bodies('/v1/auth/email/request'), hasLength(2));
    expect(key('code-field'), findsOneWidget);
    await _enter(tester, '111111');
    expect(find.textContaining('4 attempts left'), findsOneWidget);
  });

  testWidgets('resend_waits_thirty_seconds_then_requests_again', (
    tester,
  ) async {
    final h = await _toCodeStep(tester);
    TextButton resend() => tester.widget(key('code-resend'));
    expect(resend().onPressed, isNull);
    expect(find.textContaining('30'), findsWidgets);

    await tester.pump(const Duration(seconds: 29));
    expect(resend().onPressed, isNull);
    await tester.pump(const Duration(seconds: 2));
    expect(resend().onPressed, isNotNull);

    await tester.tap(key('code-resend'));
    await tester.pumpAndSettle();
    expect(h.api.bodies('/v1/auth/email/request'), hasLength(2));
    expect(resend().onPressed, isNull);
  });

  testWidgets('request_rate_limited_uses_retry_after_and_stays_on_email', (
    tester,
  ) async {
    await _toCodeStep(
      tester,
      api: AuthApi(requestStatus: 429, requestRetryAfter: '600'),
    );
    expect(find.textContaining('10 minutes'), findsOneWidget);
    expect(key('code-field'), findsNothing);
    expect(key('email-field'), findsOneWidget);
  });

  testWidgets('verify_rate_limited_shows_wait_and_keeps_code_step', (
    tester,
  ) async {
    await _toCodeStep(tester, api: AuthApi(verifyResults: [429]));
    await _enter(tester, '111111');
    expect(find.textContaining('2 minutes'), findsOneWidget);
    expect(key('code-field'), findsOneWidget);
  });

  testWidgets('invalid_or_disposable_email_messages', (tester) async {
    await _toCodeStep(
      tester,
      api: AuthApi(requestStatus: 400, requestCode: 'disposable_email'),
    );
    expect(find.textContaining('permanent email'), findsOneWidget);
  });

  testWidgets('screen_never_says_whether_an_account_exists', (tester) async {
    await _toCodeStep(tester);
    expect(find.textContaining('exist'), findsNothing);
    expect(find.textContaining('If that address'), findsOneWidget);
  });

  testWidgets('change_email_goes_back_without_session', (tester) async {
    final h = await _toCodeStep(tester);
    await tester.tap(key('code-change-email'));
    await tester.pumpAndSettle();
    expect(key('email-field'), findsOneWidget);
    expect(h.stored, isFalse);
  });

  testWidgets('double_submit_of_code_sends_once', (tester) async {
    final h = await _toCodeStep(tester);
    await tester.enterText(key('code-field'), '123456');
    await tester.tap(key('code-submit'), warnIfMissed: false);
    await tester.pumpAndSettle();
    expect(h.api.verifyCount, 1);
  });
}
