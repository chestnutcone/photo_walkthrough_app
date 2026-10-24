// Flow A: a guest opens the feed, scrolls, walks through a story, explores it,
// opens replies read-only, and is asked to sign in to follow.
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'support/e2e.dart';

void main() {
  initBinding();

  testWidgets('A: guest feed, story walk-through, explore, replies', (tester) async {
    final artist = await makeAccount('ga$uniq', name: 'Guest Artist $uniq');
    final workId = await publishStory(artist, 'Guest story $uniq', chapters: 3);
    final reader = await makeAccount('gr$uniq');
    await http('POST', '$apiBase/v1/works/$workId/chapters/0/replies',
        token: reader.token, body: {'body': 'Reader says hi $uniq'});

    await launchApp(tester);
    await pumpUntil(tester, find.byKey(ValueKey('post-card-$workId')));
    await shot(tester, 'a-guest', '01-feed');
    expect(find.text('Discover'), findsOneWidget);
    expect(find.text('Following'), findsNothing, reason: 'guests see only Discover');

    await tester.drag(find.byType(Scrollable).first, const Offset(0, -600));
    await settle(tester);
    await tester.drag(find.byType(Scrollable).first, const Offset(0, 1200));
    await settle(tester);
    await shot(tester, 'a-guest', '02-feed-scrolled');

    // Open the story from the feed.
    final card = find.byKey(ValueKey('post-card-$workId'));
    await tester.ensureVisible(card);
    await tester.tap(find.descendant(of: card, matching: find.text('Walk through')));
    await pumpUntil(tester, find.text('Begin →'));
    await shot(tester, 'a-guest', '03-story-intro');
    expect(find.text('Intro for Guest story $uniq'), findsOneWidget);

    // Step forward through chapters with the tap zones, then back.
    final size = tester.view.physicalSize / tester.view.devicePixelRatio;
    final right = Offset(size.width * 0.8, size.height * 0.35);
    final left = Offset(size.width * 0.1, size.height * 0.35);
    await tester.tap(find.text('Begin →'));
    await settle(tester, 1500);
    expect(find.text('Chapter 1'), findsWidgets);
    await shot(tester, 'a-guest', '04-chapter-1');
    await tester.tapAt(right);
    await settle(tester, 1500);
    expect(find.text('CHAPTER 02 / 03'), findsOneWidget);
    await shot(tester, 'a-guest', '05-chapter-2-stay-put');
    await tester.tapAt(left);
    await settle(tester, 1500);
    expect(find.text('CHAPTER 01 / 03'), findsOneWidget);
    await tester.tapAt(right);
    await settle(tester, 1200);
    await tester.tapAt(right);
    await settle(tester, 1200);
    await tester.tapAt(right);
    await settle(tester, 1500);
    expect(find.text('END OF STORY'), findsOneWidget);
    await shot(tester, 'a-guest', '06-end');

    // Explore mode from the end card.
    await tapText(tester, 'Explore freely');
    await settle(tester, 1000);
    expect(find.text('EXPLORE'), findsOneWidget);
    await shot(tester, 'a-guest', '07-explore');
    await tester.tap(find.byKey(const ValueKey('pin-0')).first);
    await settle(tester, 1500);
    await shot(tester, 'a-guest', '08-explore-pin');

    // Replies sheet: read-only for guests.
    final replies = find.textContaining('repl').last;
    await tester.tap(replies);
    await pumpUntil(tester, find.text('Reader says hi $uniq'));
    await shot(tester, 'a-guest', '09-replies-readonly');
    // Writing a reply as a guest asks the guest to sign in.
    await enterKey(tester, 'composer-input', 'guest typing');
    await tapKey(tester, 'composer-send');
    await pumpUntil(tester, find.byKey(const ValueKey('sso-google')));
    await shot(tester, 'a-guest', '10-reply-prompts-sign-in');
  });
}
