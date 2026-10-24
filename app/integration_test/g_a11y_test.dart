// Accessibility audit on the real app: Flutter's tap-target, label and contrast
// guidelines on every main screen. Failures are recorded as A11Y_FAIL lines
// (collected into docs/review/e2e-report.md), the test fails only at the end.
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'support/e2e.dart';

final _violations = <String>[];

Future<void> audit(WidgetTester tester, String screen) async {
  await settle(tester, 800);
  final handle = tester.ensureSemantics();
  final checks = <String, AccessibilityGuideline>{
    'tap-target-48dp': androidTapTargetGuideline,
    'labeled-tap-target': labeledTapTargetGuideline,
    'text-contrast': textContrastGuideline,
  };
  for (final e in checks.entries) {
    try {
      await expectLater(tester, meetsGuideline(e.value));
    } on TestFailure catch (f) {
      final msg = f.message?.replaceAll('\n', ' ').trim() ?? '';
      _violations.add('$screen | ${e.key} | ${msg.length > 400 ? msg.substring(0, 400) : msg}');
      // ignore: avoid_print
      print('A11Y_FAIL $screen | ${e.key} | $msg');
    }
  }
  await shot(tester, 'a11y', screen);
  handle.dispose();
}

void main() {
  initBinding();

  testWidgets('guest screens', (tester) async {
    final artist = await makeAccount('aa$uniq', name: 'A11y Artist $uniq');
    final workId = await publishStory(artist, 'A11y story $uniq');
    await launchApp(tester);
    await pumpUntil(tester, find.byKey(ValueKey('post-card-$workId')));
    await audit(tester, 'guest-feed');
    await tester.tap(find.byKey(ValueKey('follow-pill-$workId')));
    await pumpUntil(tester, find.byKey(const ValueKey('sso-google')));
    await audit(tester, 'sign-in-sheet');
    await tapKey(tester, 'sso-email');
    await pumpUntil(tester, find.byKey(const ValueKey('email-field')));
    await audit(tester, 'email-entry');
    goTo(tester, '/story/$workId');
    await pumpUntil(tester, find.text('Begin →'));
    await audit(tester, 'story-intro');
    await tester.tap(find.text('Begin →'));
    await settle(tester, 1500);
    await audit(tester, 'story-chapter');
    goTo(tester, '/search');
    await pumpUntil(tester, find.byKey(const ValueKey('search-field')));
    await audit(tester, 'search');
    goTo(tester, '/artist/${artist.handle}');
    await pumpUntil(tester, find.byKey(const ValueKey('portfolio-follow')));
    await audit(tester, 'portfolio-guest');
  });

  testWidgets('signed-in screens', (tester) async {
    final me = await makeAccount('ab$uniq', name: 'A11y Me $uniq');
    await publishStory(me, 'A11y own story $uniq');
    final other = await makeAccount('ac$uniq');
    await http('PUT', '$apiBase/v1/artists/${me.id}/follow', token: other.token);
    await launchApp(tester, signedInAs: me, picker: FakePhotoPicker(() => testPhoto()));
    await pumpUntil(tester, find.byKey(const ValueKey('tab-following')));
    await audit(tester, 'signed-in-feed');
    goTo(tester, '/notifications');
    await pumpUntil(tester, find.byKey(const ValueKey('notifications-back')));
    await settle(tester, 1500);
    await audit(tester, 'notifications');
    goTo(tester, '/settings');
    await pumpUntil(tester, find.byKey(const ValueKey('export-request')));
    await audit(tester, 'settings');
    goTo(tester, '/profile/edit');
    await pumpUntil(tester, find.byKey(const ValueKey('edit-bio')));
    await audit(tester, 'edit-profile');
    goTo(tester, '/artist/${me.handle}');
    await pumpUntil(tester, find.byKey(const ValueKey('portfolio-edit')));
    await audit(tester, 'portfolio-own');
    goTo(tester, '/create');
    await pumpUntil(tester, find.byKey(const ValueKey('pick-library')));
    await audit(tester, 'create-pick');
    await tapKey(tester, 'pick-library');
    await pumpUntil(tester, find.byKey(const ValueKey('photo-box')));
    await audit(tester, 'create-photo');
  });

  testWidgets('onboarding screens', (tester) async {
    await launchApp(tester);
    final acct = await makeAccount('ad$uniq');
    // A signed-in but not onboarded account lands on onboarding.
    final r = await http('POST', '$apiBase/v1/auth/sso',
        body: {'provider': 'fake', 'idToken': 'fake:nb$uniq:nb$uniq@example.com', 'nonce': 'n'});
    await launchApp(
      tester,
      signedInAs: Account(r['data']['accessToken'] as String,
          r['data']['refreshToken'] as String, '', ''),
    );
    await pumpUntil(tester, find.byKey(const ValueKey('welcome-continue')));
    await audit(tester, 'onboarding-welcome');
    await tapKey(tester, 'welcome-continue');
    await pumpUntil(tester, find.byKey(const ValueKey('name-field')));
    await audit(tester, 'onboarding-profile');
    expect(acct, isNotNull);
  });

  tearDownAll(() {
    // ignore: avoid_print
    print('A11Y_SUMMARY ${_violations.length} violations');
  });
}
