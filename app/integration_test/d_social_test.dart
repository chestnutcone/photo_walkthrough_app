// Flows D, H, J, K: replies, notifications, reporting, suspended account.
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'support/e2e.dart';

Finder _replyPill() =>
    find.textContaining(RegExp(r'repl', caseSensitive: false)).last;

Future<void> openStoryChapter(WidgetTester tester, String workId) async {
  goTo(tester, '/story/$workId');
  await pumpUntil(tester, find.text('Begin →'));
  await tester.tap(find.text('Begin →'));
  await settle(tester, 1500);
}

void main() {
  initBinding();

  testWidgets('D: post a reply and delete own reply', (tester) async {
    final artist = await makeAccount('da$uniq');
    final workId = await publishStory(artist, 'Reply story $uniq');
    final me = await makeAccount('dm$uniq', name: 'Replier $uniq');
    await launchApp(tester, signedInAs: me);
    await pumpUntil(tester, find.byKey(const ValueKey('tab-following')));
    await openStoryChapter(tester, workId);
    await tester.tap(_replyPill());
    await pumpUntil(tester, find.byKey(const ValueKey('composer-input')));
    await shot(tester, 'd-replies', '01-sheet-empty');
    await enterKey(tester, 'composer-input', 'My reply $uniq');
    await tapKey(tester, 'composer-send');
    await pumpUntil(tester, find.text('My reply $uniq'));
    await shot(tester, 'd-replies', '02-posted');
    final del = find.byWidgetPredicate(
      (w) =>
          w.key is ValueKey<String> &&
          (w.key! as ValueKey<String>).value.startsWith('delete-'),
    );
    await pumpUntil(tester, del);
    await tester.tap(del.first);
    await pumpUntil(tester, find.text('Delete this reply?'));
    await shot(tester, 'd-replies', '03-delete-confirm');
    await tester.tap(find.text('Delete').last);
    await pumpUntilGone(tester, find.text('My reply $uniq'));
    await shot(tester, 'd-replies', '04-deleted');
  });

  testWidgets('J: report a story as a signed-in user', (tester) async {
    final artist = await makeAccount('ja$uniq');
    final workId = await publishStory(artist, 'Report me $uniq');
    final me = await makeAccount('jm$uniq');
    await launchApp(tester, signedInAs: me);
    await pumpUntil(tester, find.byKey(const ValueKey('tab-following')));
    goTo(tester, '/story/$workId');
    await pumpUntil(tester, find.byKey(const ValueKey('story-menu')));
    await tapKey(tester, 'story-menu');
    await shot(tester, 'j-report', '01-menu');
    await tapKey(tester, 'story-menu-report');
    await pumpUntil(tester, find.byKey(const ValueKey('report-reason-spam')));
    await shot(tester, 'j-report', '02-reasons');
    await tapKey(tester, 'report-reason-spam');
    await tapKey(tester, 'report-send');
    await pumpUntil(tester, find.byKey(const ValueKey('report-done')));
    await shot(tester, 'j-report', '03-thanks');
  });

  testWidgets(
    'H: notifications for a reply and a follow deep-link to the chapter',
    (tester) async {
      final owner = await makeAccount('ho$uniq', name: 'Owner $uniq');
      final workId = await publishStory(owner, 'Notify story $uniq');
      final other = await makeAccount('hx$uniq', name: 'Other $uniq');
      await http(
        'PUT',
        '$apiBase/v1/artists/${owner.id}/follow',
        token: other.token,
      );
      await http(
        'POST',
        '$apiBase/v1/works/$workId/chapters/1/replies',
        token: other.token,
        body: {'body': 'Notify reply $uniq'},
      );

      await launchApp(tester, signedInAs: owner);
      await pumpUntil(tester, find.byKey(const ValueKey('bell-button')));
      await pumpUntil(tester, find.byKey(const ValueKey('unread-badge')));
      await shot(tester, 'h-notifications', '01-bell-badge');
      await tapKey(tester, 'bell-button');
      final items = find.byWidgetPredicate(
        (w) =>
            w.key is ValueKey<String> &&
            (w.key! as ValueKey<String>).value.startsWith('notification-') &&
            !(w.key! as ValueKey<String>).value.endsWith('-unread'),
      );
      await pumpUntil(tester, items);
      await settle(tester, 1000);
      expect(items.evaluate().length, greaterThanOrEqualTo(2));
      await shot(tester, 'h-notifications', '02-list');
      // Tap the reply notification (mentions "replied").
      final reply = find.textContaining('repl', findRichText: true);
      await tester.tap(reply.first);
      await pumpUntil(tester, find.text('Notify reply $uniq'));
      await shot(tester, 'h-notifications', '03-deep-linked-replies');
    },
  );

  testWidgets('K: a suspended account sees the suspended screen', (
    tester,
  ) async {
    final me = await makeAccount('ks$uniq');
    await launchApp(tester, signedInAs: me);
    await pumpUntil(tester, find.byKey(const ValueKey('tab-following')));
    await tester.runAsync(() => suspendHandle(me.handle));
    await tester.runAsync(
      () => Future<void>.delayed(const Duration(seconds: 2)),
    );
    goTo(tester, '/notifications');
    await pumpUntil(tester, find.byKey(const ValueKey('suspended-support')));
    await shot(tester, 'k-suspended', '01-suspended-screen');
    await tapKey(tester, 'suspended-guest');
    await pumpUntil(tester, find.text('Discover'));
    await shot(tester, 'k-suspended', '02-continue-as-guest');
  });
}
