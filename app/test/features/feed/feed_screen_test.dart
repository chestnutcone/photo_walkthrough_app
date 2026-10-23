import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:vela_app/core/api/api_providers.dart';
import 'package:vela_app/features/feed/feed_controller.dart';
import 'package:vela_app/features/feed/feed_screen.dart';

import '../../core/support/fakes.dart';
import 'support/feed_harness.dart';

ResponseBody _feed(RequestOptions o, List<Map<String, Object?>> works) =>
    jsonResponse(200, feedPage(works));

Future<void> _scrollToEnd(WidgetTester tester) async {
  for (var i = 0; i < 4; i++) {
    await tester.drag(find.byType(CustomScrollView), const Offset(0, -2500));
    await tester.pump(const Duration(milliseconds: 50));
  }
}

void main() {
  testWidgets('renders_cards_with_title_meta_and_pin_dots', (tester) async {
    await pumpFeed(tester, feed: (o) => _feed(o, [feedWork('w1')]));
    expect(find.text('Title w1'), findsOneWidget);
    expect(find.text('Ines Alvarez'), findsOneWidget);
    expect(find.text('3 CHAPTERS · 2 REPLIES'), findsOneWidget);
    expect(find.byKey(const ValueKey('pin-dot-w1-0')), findsOneWidget);
    expect(find.byKey(const ValueKey('pin-dot-w1-1')), findsOneWidget);
    expect(find.byKey(const ValueKey('pin-dot-w1-2')), findsOneWidget);
    expect(find.bySemanticsLabel('Alt for w1'), findsOneWidget);
    expect(find.text('Follow'), findsOneWidget);
    expect(find.text('Walk through'), findsOneWidget);
  });

  testWidgets('singular_counts_read_naturally', (tester) async {
    await pumpFeed(
      tester,
      feed: (o) => _feed(o, [feedWork('w1', chapters: 1)]),
    );
    expect(find.text('1 CHAPTER · 0 REPLIES'), findsOneWidget);
  });

  testWidgets('guest_sees_only_discover_tab', (tester) async {
    final h = await pumpFeed(tester, feed: (o) => _feed(o, [feedWork('w1')]));
    expect(find.byKey(const ValueKey('tab-following')), findsNothing);
    expect(find.byKey(const ValueKey('tab-discover')), findsOneWidget);
    expect(h.feedRequests().single.queryParameters['tab'], 'discover');
  });

  testWidgets('signed_in_defaults_to_following_and_can_switch', (tester) async {
    final h = await pumpFeed(
      tester,
      signedIn: true,
      feed: (o) => _feed(o, [feedWork('w-${o.queryParameters['tab']}')]),
    );
    expect(h.feedRequests().single.queryParameters['tab'], 'following');
    expect(h.feedRequests().single.cancelToken?.isCancelled, isFalse);
    expect(find.text('Title w-following'), findsOneWidget);
    await tester.tap(find.byKey(const ValueKey('tab-discover')));
    await tester.pumpAndSettle();
    expect(find.text('Title w-discover'), findsOneWidget);
  });

  testWidgets('follow_pill_for_guest_prompts_sign_in', (tester) async {
    final h = await pumpFeed(tester, feed: (o) => _feed(o, [feedWork('w1')]));
    await tester.tap(followPill('w1'));
    await tester.pumpAndSettle();
    expect(h.signInLaunches, hasLength(1));
    expect(h.followRequests(), isEmpty);
    expect(pillText(['w1'], 'Follow'), findsOneWidget);
  });

  testWidgets('guest_follow_completes_after_successful_sign_in', (
    tester,
  ) async {
    final h = await pumpFeed(
      tester,
      signInSucceeds: true,
      feed: (o) => _feed(o, [feedWork('w1')]),
    );
    await tester.tap(followPill('w1'));
    await tester.pumpAndSettle();
    expect(h.followRequests().single.method, 'PUT');
  });

  testWidgets('follow_toggles_optimistically_and_rolls_back_on_error_with_'
      'visible_message', (tester) async {
    final gate = Completer<void>();
    final h = await pumpFeed(
      tester,
      signedIn: true,
      feed: (o) => _feed(o, [
        feedWork('w1'),
        feedWork('w2'), // same author: must follow along
      ]),
      follow: (o) async {
        await gate.future;
        return jsonResponse(500, errEnvelope('server_error'));
      },
    );
    await tester.tap(followPill('w1'));
    await tester.pump();
    await tester.pump(const Duration(milliseconds: 10));
    // Optimistic: both cards by this author flip before the server answers.
    expect(pillText(['w1', 'w2'], 'Following'), findsNWidgets(2));
    expect(h.followRequests(), hasLength(1));

    gate.complete();
    await tester.pumpAndSettle();
    expect(pillText(['w1', 'w2'], 'Following'), findsNothing);
    expect(pillText(['w1', 'w2'], 'Follow'), findsNWidgets(2));
    expect(find.byType(SnackBar), findsOneWidget);
    expect(find.textContaining("Couldn't update follow"), findsOneWidget);
  });

  testWidgets('unfollow_uses_delete', (tester) async {
    final h = await pumpFeed(
      tester,
      signedIn: true,
      feed: (o) => _feed(o, [feedWork('w1', isFollowing: true)]),
    );
    expect(pillText(['w1'], 'Following'), findsOneWidget);
    await tester.tap(followPill('w1'));
    await tester.pumpAndSettle();
    expect(h.followRequests().single.method, 'DELETE');
    expect(pillText(['w1'], 'Follow'), findsOneWidget);
  });

  testWidgets('scrolling_to_end_loads_next_page_once', (tester) async {
    final page2 = Completer<void>();
    final h = await pumpFeed(
      tester,
      feed: (o) async {
        if (o.queryParameters['cursor'] == 'c1') {
          await page2.future;
          return jsonResponse(
            200,
            feedPage([
              feedWork('w4'),
              feedWork('w1'), // duplicate id must be dropped
            ]),
          );
        }
        return jsonResponse(
          200,
          feedPage([for (var i = 1; i <= 3; i++) feedWork('w$i')], next: 'c1'),
        );
      },
    );
    await _scrollToEnd(tester);
    expect(
      h.feedRequests().where((r) => r.queryParameters['cursor'] == 'c1'),
      hasLength(1),
    );
    page2.complete();
    await tester.pumpAndSettle();
    await _scrollToEnd(tester);
    expect(h.feedRequests(), hasLength(2));
    final container = ProviderScope.containerOf(
      tester.element(find.byType(FeedScreen)),
    );
    final ids = container
        .read(feedControllerProvider(FeedTab.discover))
        .items
        .map((w) => w.id);
    expect(ids, ['w1', 'w2', 'w3', 'w4']);
  });

  testWidgets('page_two_error_keeps_items_and_offers_inline_retry', (
    tester,
  ) async {
    var fail = true;
    await pumpFeed(
      tester,
      feed: (o) {
        if (o.queryParameters['cursor'] == 'c1') {
          return fail
              ? jsonResponse(500, errEnvelope('server_error'))
              : jsonResponse(200, feedPage([feedWork('w9')]));
        }
        return jsonResponse(
          200,
          feedPage([for (var i = 1; i <= 3; i++) feedWork('w$i')], next: 'c1'),
        );
      },
    );
    await _scrollToEnd(tester);
    expect(card('w3'), findsOneWidget);
    final retry = find.byKey(const ValueKey('more-retry'));
    expect(retry, findsOneWidget);
    fail = false;
    await tester.ensureVisible(retry);
    await tester.tap(retry);
    await tester.pumpAndSettle();
    await _scrollToEnd(tester);
    expect(card('w9'), findsOneWidget);
    expect(retry, findsNothing);
  });

  testWidgets('pull_to_refresh_reloads', (tester) async {
    var round = 0;
    final h = await pumpFeed(
      tester,
      feed: (o) {
        round++;
        return _feed(o, [feedWork('r$round')]);
      },
    );
    expect(card('r1'), findsOneWidget);
    await tester.fling(
      find.byType(CustomScrollView),
      const Offset(0, 400),
      1000,
    );
    await tester.pumpAndSettle();
    expect(h.feedRequests(), hasLength(2));
    expect(card('r2'), findsOneWidget);
    expect(card('r1'), findsNothing);
  });

  testWidgets('stale_page_response_after_refresh_is_ignored', (tester) async {
    final slowPage2 = Completer<void>();
    var firstLoads = 0;
    await pumpFeed(
      tester,
      feed: (o) async {
        if (o.queryParameters['cursor'] == 'c1') {
          await slowPage2.future;
          return jsonResponse(200, feedPage([feedWork('stale')]));
        }
        firstLoads++;
        return jsonResponse(
          200,
          feedPage([
            for (var i = 1; i <= 3; i++) feedWork('f$firstLoads-$i'),
          ], next: firstLoads == 1 ? 'c1' : null),
        );
      },
    );
    await _scrollToEnd(tester); // starts page 2 (slow)
    for (var i = 0; i < 4; i++) {
      await tester.drag(find.byType(CustomScrollView), const Offset(0, 2500));
      await tester.pump(const Duration(milliseconds: 50));
    }
    await tester.fling(
      find.byType(CustomScrollView),
      const Offset(0, 400),
      1000,
    );
    await tester.pump(const Duration(seconds: 2)); // spinner never settles
    slowPage2.complete();
    await tester.pumpAndSettle();
    await _scrollToEnd(tester);
    expect(card('stale'), findsNothing);
    expect(card('f2-3'), findsOneWidget);
  });

  testWidgets('empty_following_shows_discover_hint', (tester) async {
    await pumpFeed(tester, signedIn: true, feed: (o) => _feed(o, const []));
    expect(find.textContaining('Follow artists'), findsOneWidget);
    await tester.tap(find.byKey(const ValueKey('hint-discover')));
    await tester.pumpAndSettle();
    expect(find.byKey(const ValueKey('tab-discover')), findsOneWidget);
  });

  testWidgets('empty_discover_is_friendly', (tester) async {
    await pumpFeed(tester, feed: (o) => _feed(o, const []));
    expect(find.text('Nothing here yet'), findsOneWidget);
  });

  testWidgets('error_shows_retry_that_reloads', (tester) async {
    var fail = true;
    await pumpFeed(
      tester,
      feed: (o) => fail
          ? jsonResponse(500, errEnvelope('server_error'))
          : _feed(o, [feedWork('w1')]),
    );
    expect(find.text("Couldn't load the feed"), findsOneWidget);
    fail = false;
    await tester.tap(find.byKey(const ValueKey('feed-retry')));
    await tester.pumpAndSettle();
    expect(card('w1'), findsOneWidget);
  });

  testWidgets('network_error_shows_offline_message', (tester) async {
    await pumpFeed(
      tester,
      feed: (o) => throw DioException.connectionError(
        requestOptions: o,
        reason: 'offline',
      ),
    );
    expect(find.text("You're offline"), findsOneWidget);
    expect(find.byKey(const ValueKey('feed-retry')), findsOneWidget);
  });

  testWidgets('loading_shows_skeleton', (tester) async {
    final gate = Completer<void>();
    tester.view.physicalSize = phone;
    await pumpFeed(
      tester,
      feed: (o) async {
        await gate.future;
        return _feed(o, [feedWork('w1')]);
      },
    );
    expect(find.byKey(const ValueKey('feed-skeleton')), findsOneWidget);
    gate.complete();
    await tester.pumpAndSettle();
    expect(find.byKey(const ValueKey('feed-skeleton')), findsNothing);
  });

  testWidgets('unread_badge_shows_count', (tester) async {
    await pumpFeed(
      tester,
      signedIn: true,
      unread: 7,
      feed: (o) => _feed(o, [feedWork('w1')]),
    );
    expect(find.byKey(const ValueKey('unread-badge')), findsOneWidget);
    expect(find.text('7'), findsOneWidget);
  });

  testWidgets('unread_badge_caps_at_99_plus', (tester) async {
    await pumpFeed(
      tester,
      signedIn: true,
      unread: 250,
      feed: (o) => _feed(o, [feedWork('w1')]),
    );
    expect(find.text('99+'), findsOneWidget);
  });

  testWidgets('unread_badge_hidden_for_zero_and_guests', (tester) async {
    await pumpFeed(tester, unread: 5, feed: (o) => _feed(o, [feedWork('w1')]));
    expect(find.byKey(const ValueKey('bell-button')), findsNothing);
    expect(find.byKey(const ValueKey('unread-badge')), findsNothing);
  });

  testWidgets('search_and_bell_navigate', (tester) async {
    final h = await pumpFeed(
      tester,
      signedIn: true,
      feed: (o) => _feed(o, [feedWork('w1')]),
    );
    await tester.tap(find.byKey(const ValueKey('search-button')));
    await tester.pumpAndSettle();
    expect(find.text('route:search'), findsOneWidget);
    h.router.go('/');
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const ValueKey('bell-button')));
    await tester.pumpAndSettle();
    expect(find.text('route:notifications'), findsOneWidget);
  });

  testWidgets('card_and_author_taps_navigate', (tester) async {
    final h = await pumpFeed(tester, feed: (o) => _feed(o, [feedWork('w1')]));
    await tester.tap(find.byKey(const ValueKey('author-w1')));
    await tester.pumpAndSettle();
    expect(find.text('route:artist/ines'), findsOneWidget);
    h.router.go('/');
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const ValueKey('photo-w1')));
    await tester.pumpAndSettle();
    expect(find.text('route:story/w1'), findsOneWidget);
  });

  testWidgets('signing_in_reveals_following_tab_and_signing_out_hides_it', (
    tester,
  ) async {
    final h = await pumpFeed(
      tester,
      feed: (o) => _feed(o, [feedWork('w-${o.queryParameters['tab']}')]),
    );
    expect(find.byKey(const ValueKey('tab-following')), findsNothing);
    final container = ProviderScope.containerOf(
      tester.element(find.byType(FeedScreen)),
    );
    await container
        .read(sessionProvider.notifier)
        .signIn(accessToken: 'A', refreshToken: 'R', expiresIn: 3600);
    await tester.pumpAndSettle();
    expect(find.byKey(const ValueKey('tab-following')), findsOneWidget);
    expect(h.feedRequests().last.queryParameters['tab'], 'following');
    await container.read(sessionProvider.notifier).clear();
    await tester.pumpAndSettle();
    expect(find.byKey(const ValueKey('tab-following')), findsNothing);
    expect(h.feedRequests().last.queryParameters['tab'], 'discover');
  });
}
