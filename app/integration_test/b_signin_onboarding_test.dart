// Flows B and C: sign in (fake SSO and email code), onboarding, follow an
// artist and see the Following tab.
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'support/e2e.dart';

Future<void> completeOnboarding(
  WidgetTester tester,
  String flow,
  String handle, {
  String? followId,
}) async {
  await pumpUntil(tester, find.byKey(const ValueKey('welcome-continue')));
  await shot(tester, flow, '03-onboarding-welcome');
  await tapKey(tester, 'welcome-continue');
  await enterKey(tester, 'name-field', 'Tester $handle');
  await enterKey(tester, 'handle-field', handle);
  await settle(tester, 2500);
  await tapKey(tester, 'age-check');
  await tapKey(tester, 'terms-check');
  await shot(tester, flow, '04-onboarding-profile');
  await tapKey(tester, 'profile-continue');
  await pumpUntil(tester, find.byKey(const ValueKey('follow-finish')));
  await settle(tester, 1500);
  var picked = 0;
  if (followId != null) {
    await tapKey(tester, 'artist-$followId');
    picked++;
  }
  // The server wants at least three follows; pick others from the list.
  final rows = find.byWidgetPredicate(
    (w) =>
        w.key is ValueKey<String> &&
        (w.key! as ValueKey<String>).value.startsWith('artist-') &&
        (w.key! as ValueKey<String>).value != 'artist-$followId',
  );
  for (var i = 0; picked < 3 && i < rows.evaluate().length; i++) {
    await tester.ensureVisible(rows.at(i));
    await tester.tap(rows.at(i));
    await settle(tester, 300);
    picked++;
  }
  await shot(tester, flow, '05-onboarding-follow');
  await tapKey(tester, 'follow-finish');
}

void main() {
  initBinding();

  testWidgets('B/C: SSO sign-in, onboarding, follow, Following tab', (tester) async {
    final artist = await makeAccount('fa$uniq', name: 'Followable $uniq');
    final workId = await publishStory(artist, 'Followable story $uniq');

    await launchApp(tester);
    await pumpUntil(tester, find.byKey(ValueKey('post-card-$workId')));
    // Follow as a guest: sign-in sheet opens.
    await tester.tap(find.byKey(ValueKey('follow-pill-$workId')));
    await pumpUntil(tester, find.byKey(const ValueKey('sso-google')));
    await shot(tester, 'b-signin', '01-sign-in-sheet');
    expect(find.byKey(const ValueKey('sso-email')), findsOneWidget);
    await tapKey(tester, 'sso-google');
    await completeOnboarding(tester, 'b-signin', 'ob$uniq', followId: artist.id);

    // Signed in and onboarded: feed with a Following tab that lists the artist.
    await pumpUntil(tester, find.byKey(const ValueKey('tab-following')));
    await shot(tester, 'b-signin', '06-feed-signed-in');
    await tapKey(tester, 'tab-following');
    await pumpUntil(tester, find.byKey(ValueKey('post-card-$workId')));
    await shot(tester, 'c-follow', '01-following-tab');
    expect(find.text('Following'), findsWidgets);

    // Unfollow from the card and refresh: the Following tab empties out.
    await tester.tap(find.byKey(ValueKey('follow-pill-$workId')));
    await settle(tester, 1500);
    await shot(tester, 'c-follow', '02-unfollowed');
  });

  testWidgets('B: email code sign-in for a new account', (tester) async {
    final email = 'mail$uniq@example.com';
    await launchApp(tester);
    final pill = find.byWidgetPredicate((w) =>
        w.key is ValueKey<String> &&
        (w.key! as ValueKey<String>).value.startsWith('follow-pill-'));
    await pumpUntil(tester, pill);
    await tester.tap(pill.first);
    await pumpUntil(tester, find.byKey(const ValueKey('sso-email')));
    await tapKey(tester, 'sso-email');
    await enterKey(tester, 'email-field', email);
    await tapKey(tester, 'email-send');
    await pumpUntil(tester, find.byKey(const ValueKey('code-field')));
    await shot(tester, 'b-signin', '10-email-code-entry');
    await settle(tester, 1000);
    final code = await tester.runAsync(latestEmailCode);
    await tester.enterText(find.byKey(const ValueKey('code-field')), code!);
    await settle(tester, 300);
    // The six-digit field submits itself on the last digit.
    await completeOnboarding(tester, 'b-signin-email', 'em$uniq');
    await pumpUntil(tester, find.byKey(const ValueKey('tab-following')));
    await shot(tester, 'b-signin', '11-email-signed-in');
  });
}
