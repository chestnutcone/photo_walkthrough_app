import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import '../../core/support/fakes.dart';
import 'support/feed_harness.dart';

ResponseBody _page(List<Map<String, Object?>> works, {String? next}) =>
    jsonResponse(200, feedPage(works, next: next));

Future<void> _pull(WidgetTester tester) async {
  await tester.fling(find.byType(CustomScrollView), const Offset(0, 400), 1000);
  await tester.pump(const Duration(seconds: 1));
  await tester.pumpAndSettle();
}

void main() {
  testWidgets('offline_refresh_keeps_items_and_shows_snackbar', (tester) async {
    var offline = false;
    await pumpFeed(
      tester,
      feed: (o) {
        if (offline) {
          throw DioException.connectionError(requestOptions: o, reason: 'x');
        }
        return _page([feedWork('w1')]);
      },
    );
    offline = true;
    await _pull(tester);
    expect(card('w1'), findsOneWidget);
    expect(find.text("You're offline. Couldn't refresh."), findsOneWidget);
  });

  testWidgets('short_first_page_auto_fills_without_scrolling', (tester) async {
    final h = await pumpFeed(
      tester,
      feed: (o) => o.queryParameters['cursor'] == 'c1'
          ? _page([feedWork('w2')])
          : _page([feedWork('w1')], next: 'c1'),
    );
    expect(h.feedRequests(), hasLength(2));
    expect(card('w2'), findsOneWidget);
  });

  testWidgets('refreshing_an_empty_feed_does_not_flash_the_skeleton', (
    tester,
  ) async {
    final gate = Completer<void>();
    var calls = 0;
    await pumpFeed(
      tester,
      feed: (o) async {
        if (++calls == 2) await gate.future;
        return _page(const []);
      },
    );
    expect(find.text('Nothing here yet'), findsOneWidget);
    await tester.fling(
      find.byType(CustomScrollView),
      const Offset(0, 400),
      1000,
    );
    for (var i = 0; i < 6; i++) {
      await tester.pump(const Duration(milliseconds: 500));
    }
    expect(calls, 2);
    expect(find.byKey(const ValueKey('feed-skeleton')), findsNothing);
    expect(find.text('Nothing here yet'), findsOneWidget);
    gate.complete();
    await tester.pumpAndSettle();
  });

  testWidgets('switching_tabs_scrolls_back_to_top', (tester) async {
    await pumpFeed(
      tester,
      signedIn: true,
      feed: (o) => _page([for (var i = 1; i <= 4; i++) feedWork('w$i')]),
    );
    final scroll = find.byType(CustomScrollView);
    await tester.drag(scroll, const Offset(0, -35)); // tabs still on screen
    await tester.pump();
    ScrollableState state() => tester.state(find.byType(Scrollable).first);
    expect(state().position.pixels, greaterThan(0));
    await tester.tap(find.byKey(const ValueKey('tab-discover')));
    await tester.pumpAndSettle();
    expect(state().position.pixels, 0);
  });

  testWidgets('pull_to_refresh_refetches_unread_count_but_guests_never_do', (
    tester,
  ) async {
    final h = await pumpFeed(
      tester,
      signedIn: true,
      unread: 3,
      feed: (o) => _page([feedWork('w1')]),
    );
    expect(h.adapter.count('/v1/notifications/unread-count'), 1);
    await _pull(tester);
    expect(h.adapter.count('/v1/notifications/unread-count'), 2);
  });

  testWidgets('guest_never_requests_unread_count', (tester) async {
    final h = await pumpFeed(tester, feed: (o) => _page([feedWork('w1')]));
    await _pull(tester);
    expect(h.adapter.count('/v1/notifications/unread-count'), 0);
  });
}
