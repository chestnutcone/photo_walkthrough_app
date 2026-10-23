import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import '../../core/support/fakes.dart';
import 'support/portfolio_harness.dart';

Finder key(String k) => find.byKey(ValueKey(k));

void main() {
  Future<PortfolioHarness> open(WidgetTester t, {Responder? followers}) async {
    final h = await pumpPortfolio(t, me: meJson(), followers: followers);
    await t.tap(key('stat-followers'));
    await t.pumpAndSettle();
    return h;
  }

  testWidgets('rows_paginate_by_cursor', (t) async {
    final h = await open(
      t,
      followers: (o) => o.queryParameters['cursor'] == null
          ? jsonResponse(
              200,
              listPage([
                for (var i = 0; i < 3; i++)
                  profileJson(id: 'f$i', handle: 'h$i', name: 'Fan $i'),
              ], next: 'n2'),
            )
          : jsonResponse(
              200,
              listPage([profileJson(id: 'f9', handle: 'h9', name: 'Fan 9')]),
            ),
    );
    expect(key('follower-row-f0'), findsOneWidget);
    expect(key('follower-row-f9'), findsOneWidget);
    final cursors = h
        .requestsTo('/followers')
        .map((r) => r.queryParameters['cursor'])
        .toList();
    expect(cursors.contains('n2'), isTrue);
  });

  testWidgets('tap_row_goes_to_that_artist', (t) async {
    final h = await open(
      t,
      followers: (_) => jsonResponse(
        200,
        listPage([profileJson(id: 'f1', handle: 'teo', name: 'Teo Marsh')]),
      ),
    );
    await t.tap(key('follower-row-f1'));
    await t.pumpAndSettle();
    expect(h.router.state.uri.path, '/artist/teo');
  });

  testWidgets('follow_buttons_share_state_with_the_profile', (t) async {
    final h = await open(
      t,
      followers: (_) => jsonResponse(
        200,
        listPage([
          profileJson(
            id: 'a1',
            handle: 'ines',
            name: 'Ines Alvarez',
            isFollowing: false,
          ),
        ]),
      ),
    );
    await t.tap(key('follower-follow-a1'));
    await t.pumpAndSettle();
    expect(h.requestsTo('/a1/follow'), hasLength(1));
    expect(
      find.descendant(
        of: key('follower-follow-a1'),
        matching: find.text('Following'),
      ),
      findsOneWidget,
    );
    // Close the sheet: the profile's own button follows the same state.
    await t.tapAt(const Offset(10, 10));
    await t.pumpAndSettle();
    expect(
      find.descendant(
        of: key('portfolio-follow'),
        matching: find.text('Following'),
      ),
      findsOneWidget,
    );
  });

  testWidgets('own_row_has_no_follow_button', (t) async {
    await open(
      t,
      followers: (_) => jsonResponse(
        200,
        listPage([
          profileJson(
            id: 'a9',
            handle: 'me',
            name: 'Me Myself',
            isFollowing: null,
          ),
        ]),
      ),
    );
    expect(key('follower-row-a9'), findsOneWidget);
    expect(key('follower-follow-a9'), findsNothing);
  });

  testWidgets('failure_shows_retry', (t) async {
    var fail = true;
    await open(
      t,
      followers: (_) => fail
          ? jsonResponse(500, errEnvelope('server_error'))
          : jsonResponse(
              200,
              listPage([profileJson(id: 'f1', handle: 't', name: 'T')]),
            ),
    );
    expect(key('followers-retry'), findsOneWidget);
    fail = false;
    await t.tap(key('followers-retry'));
    await t.pumpAndSettle();
    expect(key('follower-row-f1'), findsOneWidget);
  });

  testWidgets('guest_tapping_followers_must_sign_in', (t) async {
    final h = await pumpPortfolio(t);
    await t.tap(key('stat-followers'));
    await t.pumpAndSettle();
    expect(h.signInLaunches, hasLength(1));
    expect(key('followers-sheet'), findsNothing);
  });
}
