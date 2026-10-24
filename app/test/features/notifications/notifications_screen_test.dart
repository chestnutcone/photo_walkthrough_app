import 'package:flutter/material.dart';
import 'package:flutter/scheduler.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:vela_app/core/api/api_providers.dart';
import 'package:vela_app/features/notifications/unread_count_provider.dart';

import 'support/fake_notifications_api.dart';
import 'support/notifications_harness.dart';

Finder _key(String k) => find.byKey(ValueKey(k));

Future<void> _background(WidgetTester tester) async {
  for (final st in [
    AppLifecycleState.inactive,
    AppLifecycleState.hidden,
    AppLifecycleState.paused,
  ]) {
    tester.binding.handleAppLifecycleStateChanged(st);
  }
  await tester.pump();
}

void _comeBack(WidgetTester tester) {
  for (final st in [
    AppLifecycleState.hidden,
    AppLifecycleState.inactive,
    AppLifecycleState.resumed,
  ]) {
    tester.binding.handleAppLifecycleStateChanged(st);
  }
}

Future<void> _resume(WidgetTester tester) async {
  await _background(tester);
  _comeBack(tester);
  await tester.pump();
  await tester.pump();
}

void main() {
  testWidgets('shows_a_spinner_then_the_notifications', (tester) async {
    final h = await pumpNotifications(tester);
    expect(find.byType(CircularProgressIndicator), findsOneWidget);
    await answerList(tester, h.api, [
      notifJson('n1', type: 'follow', workId: null),
      notifJson('n2', type: 'new_story', actorId: 'a2', read: true),
    ]);
    expect(find.text('Ines a1 started following you'), findsOneWidget);
    expect(find.text('Ines a2 published Salt and Rope'), findsOneWidget);
    expect(_key('notification-n1-unread'), findsOneWidget);
    expect(_key('notification-n2-unread'), findsNothing);
    expect(find.text('2h'), findsNWidgets(2));
  });

  testWidgets('grouped_reply_item_shows_count', (tester) async {
    final h = await pumpNotifications(tester);
    await answerList(tester, h.api, [
      notifJson('n3', count: 3, ids: ['n3', 'n2', 'n1']),
    ]);
    expect(find.text('3 new replies on Salt and Rope'), findsOneWidget);
  });

  testWidgets('empty_state', (tester) async {
    final h = await pumpNotifications(tester);
    await answerList(tester, h.api, []);
    expect(find.text('No notifications yet'), findsOneWidget);
  });

  testWidgets('guest_sees_sign_in_prompt', (tester) async {
    final h = await pumpNotifications(tester, signedIn: false);
    expect(find.text('Sign in to see notifications'), findsOneWidget);
    expect(h.api.listCalls, isEmpty);
    await tester.tap(_key('notifications-sign-in'));
    await tester.pump();
    expect(h.signInLaunches, hasLength(1));
  });

  testWidgets('losing_the_session_swaps_the_list_for_the_prompt', (
    tester,
  ) async {
    final h = await pumpNotifications(tester);
    await answerList(tester, h.api, [notifJson('n1')]);
    expect(find.textContaining('replied'), findsOneWidget);
    await h.container.read(sessionProvider.notifier).clear();
    await tester.pump();
    await tester.pump();
    expect(find.text('Sign in to see notifications'), findsOneWidget);
    expect(find.textContaining('replied'), findsNothing);
  });

  testWidgets('refreshes_on_resume', (tester) async {
    final h = await pumpNotifications(tester);
    await answerList(tester, h.api, [notifJson('n1')]);
    expect(h.api.listCalls, hasLength(1));
    await _background(tester);
    expect(h.api.listCalls, hasLength(1)); // going away is not coming back
    _comeBack(tester);
    await tester.pump();
    expect(h.api.listCalls, hasLength(2));
    await answerList(tester, h.api, [notifJson('n9')], call: 1);
    expect(find.text('Ines a1 replied on Salt and Rope'), findsOneWidget);
  });

  testWidgets('resume_refresh_also_refetches_the_badge', (tester) async {
    final h = await pumpNotifications(tester);
    watchBadge(h);
    await answerList(tester, h.api, []);
    final before = h.api.unreadCalls;
    await _resume(tester);
    expect(h.api.unreadCalls, before + 1);
  });

  testWidgets('no_resume_refresh_after_leaving_the_screen', (tester) async {
    final h = await pumpNotifications(tester);
    await answerList(tester, h.api, []);
    h.router.go('/');
    await tester.pumpAndSettle();
    await _resume(tester);
    expect(h.api.listCalls, hasLength(1));
    expect(tester.takeException(), isNull);
  });

  testWidgets('pull_to_refresh_refetches_the_list_and_the_badge', (
    tester,
  ) async {
    final h = await pumpNotifications(tester);
    watchBadge(h);
    await answerList(tester, h.api, [notifJson('n1')]);
    final before = h.api.unreadCalls;
    await tester.fling(find.byType(ListView), const Offset(0, 400), 1000);
    await tester.pump(const Duration(milliseconds: 100));
    await tester.pump(const Duration(milliseconds: 500));
    expect(h.api.listCalls, hasLength(2));
    h.api.listCalls[1].reply([notifJson('n7')]);
    await tester.pumpAndSettle();
    expect(h.api.unreadCalls, greaterThan(before));
  });

  testWidgets('tap_marks_read_and_navigates_to_chapter_replies', (
    tester,
  ) async {
    final h = await pumpNotifications(tester);
    await answerList(tester, h.api, [notifJson('n1', chapter: 2)]);
    await tester.tap(_key('notification-n1'));
    await tester.pump();
    expect(h.api.markCalls.single.ids, ['n1']);
    // Optimistic: no wait for the server.
    expect(find.text('route:/story/w1?chapter=2&replies=1'), findsOneWidget);
    h.api.markCalls.single.succeed();
    await tester.pumpAndSettle();
  });

  testWidgets('tapping_a_follow_opens_the_artist', (tester) async {
    final h = await pumpNotifications(tester);
    await answerList(tester, h.api, [
      notifJson('n1', type: 'follow', workId: null),
    ]);
    await tester.tap(_key('notification-n1'));
    await tester.pump();
    expect(find.text('route:/artist/ha1'), findsOneWidget);
  });

  testWidgets('a_read_item_navigates_without_a_mark_call', (tester) async {
    final h = await pumpNotifications(tester);
    await answerList(tester, h.api, [notifJson('n1', read: true)]);
    await tester.tap(_key('notification-n1'));
    await tester.pump();
    expect(h.api.markCalls, isEmpty);
    expect(find.textContaining('route:/story/w1'), findsOneWidget);
  });

  testWidgets('a_failed_mark_read_rolls_the_dot_back', (tester) async {
    final h = await pumpNotifications(tester);
    await answerList(tester, h.api, [
      notifJson('n1', type: 'follow', workId: null),
    ]);
    await tester.tap(_key('notification-n1'));
    await tester.pump();
    h.router.pop();
    await tester.pumpAndSettle();
    expect(_key('notification-n1-unread'), findsNothing);
    h.api.markCalls.single.fail();
    await tester.pumpAndSettle();
    expect(_key('notification-n1-unread'), findsOneWidget);
  });

  testWidgets('badge_updates_after_mark_read', (tester) async {
    final api = FakeNotificationsApi()..unread = 2;
    final h = await pumpNotifications(tester, api: api);
    final badge = watchBadge(h);
    await answerList(tester, api, [
      notifJson('n1', type: 'follow', workId: null),
      notifJson('n2', type: 'follow', actorId: 'a2', workId: null),
    ]);
    await h.container.read(unreadCountProvider.future);
    expect(badge.read().value, 2);
    await tester.tap(_key('notification-n1'));
    await tester.pump();
    api.unread = 1;
    api.markCalls.single.succeed();
    await tester.pumpAndSettle();
    await h.container.read(unreadCountProvider.future);
    expect(badge.read().value, 1);
  });

  testWidgets('first_load_error_offers_retry_with_offline_wording', (
    tester,
  ) async {
    final h = await pumpNotifications(tester);
    h.api.listCalls[0].fail();
    await tester.pump();
    await tester.pump();
    expect(find.text("You're offline"), findsOneWidget);
    await tester.tap(_key('notifications-retry'));
    await tester.pump();
    h.api.listCalls[1].failServer();
    await tester.pump();
    await tester.pump();
    expect(find.text("Couldn't load notifications"), findsOneWidget);
    await tester.tap(_key('notifications-retry'));
    await tester.pump();
    await answerList(tester, h.api, [notifJson('n1')], call: 2);
    expect(find.textContaining('replied'), findsOneWidget);
  });

  testWidgets('a_failed_refresh_keeps_the_list_and_says_so', (tester) async {
    final h = await pumpNotifications(tester);
    await answerList(tester, h.api, [notifJson('n1')]);
    await _resume(tester);
    h.api.listCalls[1].fail();
    await tester.pump();
    await tester.pump();
    expect(find.textContaining('replied'), findsOneWidget);
    expect(find.text("You're offline. Couldn't refresh."), findsOneWidget);
  });

  testWidgets('reaching_the_end_loads_more_and_a_failure_can_retry', (
    tester,
  ) async {
    final h = await pumpNotifications(tester);
    await answerList(tester, h.api, [
      for (var i = 0; i < 12; i++)
        notifJson('n$i', type: 'follow', workId: null, actorId: 'a$i'),
    ], next: 'c1');
    await tester.drag(find.byType(ListView), const Offset(0, -3000));
    await tester.pump();
    expect(h.api.listCalls, hasLength(2));
    expect(h.api.listCalls[1].cursor, 'c1');
    h.api.listCalls[1].fail();
    await tester.pump();
    await tester.pump();
    await tester.drag(find.byType(ListView), const Offset(0, -3000));
    await tester.pump();
    await tester.tap(_key('more-retry'));
    await tester.pump();
    expect(h.api.listCalls, hasLength(3));
    h.api.listCalls[2].reply([
      notifJson('last', type: 'follow', workId: null, actorId: 'zz'),
    ]);
    await tester.pump();
    await tester.pump();
    await tester.drag(find.byType(ListView), const Offset(0, -6000));
    await tester.pump();
    expect(find.text('Ines zz started following you'), findsOneWidget);
  });

  testWidgets('unknown_types_render_generically_and_do_not_crash', (
    tester,
  ) async {
    final h = await pumpNotifications(tester);
    await answerList(tester, h.api, [
      notifJson('n1', type: 'mystery', workId: null),
    ]);
    expect(tester.takeException(), isNull);
    expect(find.text('Ines a1 has an update for you'), findsOneWidget);
    await tester.tap(_key('notification-n1'));
    await tester.pump();
    expect(h.api.markCalls.single.ids, ['n1']);
    expect(find.text('route:/artist/ha1'), findsOneWidget);
  });

  testWidgets('a_notification_with_no_page_says_so_and_is_still_marked_read', (
    tester,
  ) async {
    final h = await pumpNotifications(tester);
    await answerList(tester, h.api, [notifJson('n1', workId: null)]);
    await tester.tap(_key('notification-n1'));
    await tester.pump();
    expect(h.api.markCalls.single.ids, ['n1']);
    expect(find.text('This notification no longer has a page'), findsOneWidget);
    expect(find.text('route:feed'), findsNothing);
    expect(_key('notification-n1'), findsOneWidget);
    h.api.markCalls.single.succeed();
    await tester.pumpAndSettle();
  });

  testWidgets('rows_are_44_points_labelled_and_say_when_unread', (
    tester,
  ) async {
    final h = await pumpNotifications(tester);
    await answerList(tester, h.api, [
      notifJson('n1', type: 'follow', workId: null),
    ]);
    final size = tester.getSize(_key('notification-n1'));
    expect(size.height, greaterThanOrEqualTo(44));
    expect(size.width, greaterThanOrEqualTo(44));
    final handle = tester.ensureSemantics();
    expect(
      find.bySemanticsLabel('Ines a1 started following you, 2h, unread'),
      findsOneWidget,
    );
    expect(find.bySemanticsLabel('Back'), findsOneWidget);
    handle.dispose();
  });

  testWidgets('leaving_with_a_request_in_flight_is_safe', (tester) async {
    final h = await pumpNotifications(tester);
    final call = h.api.listCalls.single;
    h.router.go('/');
    await tester.pumpAndSettle();
    call.reply([notifJson('n1')]);
    await tester.pump();
    expect(tester.takeException(), isNull);
  });

  testWidgets('the_screen_never_uses_animation_controllers', (tester) async {
    // Reduced motion: only the platform refresh spinner and progress
    // indicator animate, both of which honour the platform setting.
    final h = await pumpNotifications(tester);
    await answerList(tester, h.api, [notifJson('n1')]);
    expect(SchedulerBinding.instance.transientCallbackCount, 0);
  });
}
