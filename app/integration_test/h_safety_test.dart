// Owner decisions 2026-11-03 on the device: the 24 h illegal_content rule
// (flow L) and the re-signup block after deleting an account under review
// (flow M, `signup_blocked`).
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'support/e2e.dart';

/// Reports [workId] as illegal content through the story menu and waits for
/// the acknowledgement.
Future<void> reportIllegal(
  WidgetTester tester,
  String workId,
  String flow,
  String prefix,
) async {
  goTo(tester, '/story/$workId');
  await pumpUntil(tester, find.byKey(const ValueKey('story-menu')));
  await tapKey(tester, 'story-menu');
  await tapKey(tester, 'story-menu-report');
  await tapKey(tester, 'report-reason-illegal_content');
  await shot(tester, flow, '$prefix-reason');
  await tapKey(tester, 'report-send');
  await pumpUntil(tester, find.byKey(const ValueKey('report-done')));
  await shot(tester, flow, '$prefix-acknowledged');
  await tapKey(tester, 'report-done');
}

/// What a guest (another viewer) sees: true when the story still opens.
Future<bool> guestSeesStory(String workId) async {
  try {
    await http('GET', '$apiBase/v1/works/$workId');
    return true;
  } on StateError catch (e) {
    if (e.message.contains('-> 404')) return false;
    rethrow;
  }
}

/// Opens [workId] in a fresh guest install and returns once it shows either
/// the intro or the unavailable message.
Future<void> openAsGuest(WidgetTester tester, String workId) async {
  await launchApp(tester);
  await pumpUntil(tester, find.text('Discover'));
  goTo(tester, '/story/$workId');
  await pumpUntil(
    tester,
    find.byWidgetPredicate(
      (w) =>
          w is Text &&
          (w.data == 'Begin →' || w.data == "This story isn't available"),
    ),
  );
}

void main() {
  initBinding();

  testWidgets(
    'L: illegal_content from an account under 24 h stays up; from an older one it hides',
    (tester) async {
      final artist = await makeAccount('la$uniq');
      final young = await makeAccount('ly$uniq');
      final workId = await publishStory(artist, 'Illegal young $uniq');

      // A brand-new account: acknowledged, queued, no hide for others.
      await launchApp(tester, signedInAs: young);
      await pumpUntil(tester, find.byKey(const ValueKey('tab-following')));
      await reportIllegal(tester, workId, 'l-illegal-24h', '01-young');
      expect(await tester.runAsync(() => guestSeesStory(workId)), isTrue);
      await openAsGuest(tester, workId);
      expect(find.text('Begin →'), findsOneWidget);
      await shot(tester, 'l-illegal-24h', '03-young-still-visible-to-guest');

      // The same report from an account back-dated past 24 h (short of the
      // 7-day rule) hides the story for everyone at once.
      final old = await makeAccount('lo$uniq');
      await tester.runAsync(() => backdateHandle(old.handle, 25));
      await launchApp(tester, signedInAs: old);
      await pumpUntil(tester, find.byKey(const ValueKey('tab-following')));
      await reportIllegal(tester, workId, 'l-illegal-24h', '04-old');
      expect(await tester.runAsync(() => guestSeesStory(workId)), isFalse);
      await openAsGuest(tester, workId);
      expect(find.text("This story isn't available"), findsOneWidget);
      await shot(tester, 'l-illegal-24h', '06-old-hidden-for-guest');
    },
  );

  testWidgets(
    'M: deleting an account under review blocks signing up again with it',
    (tester) async {
      final artist = await makeAccount('ma$uniq');
      final reporter = await makeAccount('mr$uniq');
      final workId = await publishStory(artist, 'Under review $uniq');
      // A pending report puts the artist's content under review.
      await http(
        'POST',
        '$apiBase/v1/reports',
        token: reporter.token,
        body: {'targetType': 'work', 'targetId': workId, 'reason': 'spam'},
      );
      final del = await http('DELETE', '$apiBase/v1/me', token: artist.token);
      expect(del['data']['state'], 'deferred_review');

      // Same sign-in identity (fake SSO subject) on a fresh install.
      await launchApp(tester, ssoSubject: 'ma$uniq-$runId');
      final pill = find.byWidgetPredicate(
        (w) =>
            w.key is ValueKey<String> &&
            (w.key! as ValueKey<String>).value.startsWith('follow-pill-'),
      );
      await pumpUntil(tester, pill);
      await tester.tap(pill.first);
      await pumpUntil(tester, find.byKey(const ValueKey('sso-google')));
      await tapKey(tester, 'sso-google');
      await pumpUntil(tester, find.byKey(const ValueKey('sign-in-error')));
      expect(
        find.textContaining('deleted while its content was under review'),
        findsOneWidget,
      );
      // Still a guest, sheet still usable (email option offered).
      expect(find.byKey(const ValueKey('sso-email')), findsOneWidget);
      await shot(tester, 'm-signup-blocked', '01-sign-in-sheet-message');
    },
  );
}
