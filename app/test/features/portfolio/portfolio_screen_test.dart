import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:vela_app/features/portfolio/dashed_box.dart';
import 'package:vela_app/features/report/report_sheet_hook.dart';

import '../../core/support/fakes.dart';
import 'support/portfolio_harness.dart';

void main() {
  Responder works(List<Map<String, Object?>> list, {String? next}) =>
      (_) => jsonResponse(200, listPage(list, next: next));

  group('PortfolioScreen', () {
    testWidgets('works_grid_and_featured_story_render', (t) async {
      await pumpPortfolio(
        t,
        works: works([workJson('w1'), workJson('w2'), workJson('w3')]),
      );
      expect(find.text('Ines Alvarez'), findsWidgets);
      expect(find.text('PHOTOGRAPHER · LISBON'), findsOneWidget);
      expect(find.text('Light and salt.'), findsOneWidget);
      expect(find.text('FEATURED STORY'), findsOneWidget);
      expect(find.byKey(const ValueKey('featured-story')), findsOneWidget);
      expect(find.text('Title w1'), findsWidgets);
      // The featured story is not repeated in the grid.
      expect(find.byKey(const ValueKey('work-tile-w1')), findsNothing);
      expect(find.byKey(const ValueKey('work-tile-w2')), findsOneWidget);
      expect(find.byKey(const ValueKey('work-tile-w3')), findsOneWidget);
      expect(find.text('ALL WORKS'), findsOneWidget);
      expect(find.text('3 CHAPTERS'), findsWidgets);
      expect(find.text('Works · 3'), findsOneWidget);
    });

    testWidgets('tapping_a_work_opens_its_story', (t) async {
      final h = await pumpPortfolio(
        t,
        works: works([workJson('w1'), workJson('w2')]),
      );
      await t.tap(find.byKey(const ValueKey('work-tile-w2')));
      await t.pumpAndSettle();
      expect(find.text('route:story/w2'), findsOneWidget);
      expect(h.router.state.uri.path, '/story/w2');
    });

    testWidgets('stats_and_followers_sheet_open', (t) async {
      await pumpPortfolio(
        t,
        me: meJson(),
        works: works([workJson('w1')]),
        followers: (_) => jsonResponse(
          200,
          listPage([
            profileJson(id: 'f1', handle: 'teo', name: 'Teo Marsh'),
            profileJson(id: 'f2', handle: 'mara', name: 'Mara Lin'),
          ]),
        ),
      );
      expect(find.text('Stories'), findsOneWidget);
      expect(find.text('Chapters'), findsOneWidget);
      expect(find.text('Following'), findsWidgets);
      expect(find.text('Followed by Teo, Mara and 10 others'), findsOneWidget);
      await t.tap(find.byKey(const ValueKey('stat-followers')));
      await t.pumpAndSettle();
      expect(find.byKey(const ValueKey('followers-sheet')), findsOneWidget);
      expect(find.text('12 followers'), findsOneWidget);
      expect(find.text('Teo Marsh'), findsOneWidget);
    });

    testWidgets('own_profile_shows_edit_and_new_story', (t) async {
      final h = await pumpPortfolio(
        t,
        handle: 'me',
        me: meJson(),
        artist: (_) => jsonResponse(
          200,
          okEnvelope(profileJson(id: 'a9', handle: 'me', isFollowing: null)),
        ),
        works: works([workJson('w1')]),
      );
      expect(find.byKey(const ValueKey('portfolio-edit')), findsOneWidget);
      expect(find.byKey(const ValueKey('portfolio-new-story')), findsOneWidget);
      expect(find.byKey(const ValueKey('portfolio-follow')), findsNothing);
      expect(find.byKey(const ValueKey('portfolio-contact')), findsNothing);
      await t.tap(find.byKey(const ValueKey('portfolio-edit')));
      await t.pumpAndSettle();
      expect(find.text('route:profile-edit'), findsOneWidget);
      h.router.pop();
      await t.pumpAndSettle();
      await t.tap(find.byKey(const ValueKey('portfolio-new-story')));
      await t.pumpAndSettle();
      expect(find.text('route:create'), findsOneWidget);
    });

    testWidgets('own_empty_works_state_shows_quota_from_api', (t) async {
      await pumpPortfolio(
        t,
        handle: 'me',
        me: meJson(used: 0, max: 7),
        artist: (_) => jsonResponse(
          200,
          okEnvelope(profileJson(id: 'a9', handle: 'me', isFollowing: null)),
        ),
      );
      expect(find.byKey(const ValueKey('works-empty')), findsOneWidget);
      expect(find.textContaining('No stories yet'), findsOneWidget);
      expect(find.text('0 of 7 stories'), findsOneWidget);
    });

    testWidgets('empty_works_box_has_a_dashed_border', (t) async {
      await pumpPortfolio(t, me: meJson());
      expect(find.byType(DashedBox), findsOneWidget);
    });

    testWidgets('foreign_empty_works_state_has_no_quota', (t) async {
      await pumpPortfolio(t, me: meJson());
      expect(find.byKey(const ValueKey('works-empty')), findsOneWidget);
      expect(find.textContaining('of 20 stories'), findsNothing);
    });

    for (final status in [404, 403, 410]) {
      testWidgets('blocked_or_missing_artist_shows_not_found_$status', (
        t,
      ) async {
        await pumpPortfolio(
          t,
          artist: (_) => jsonResponse(status, errEnvelope('whatever_$status')),
        );
        expect(
          find.byKey(const ValueKey('portfolio-not-found')),
          findsOneWidget,
        );
        expect(find.text('Artist not found'), findsOneWidget);
        expect(find.byKey(const ValueKey('portfolio-retry')), findsNothing);
      });
    }

    testWidgets('network_error_shows_retry_that_reloads', (t) async {
      var fail = true;
      final h = await pumpPortfolio(
        t,
        artist: (_) => fail
            ? throw StateError('offline')
            : jsonResponse(200, okEnvelope(profileJson())),
      );
      expect(find.byKey(const ValueKey('portfolio-retry')), findsOneWidget);
      expect(find.byKey(const ValueKey('portfolio-not-found')), findsNothing);
      fail = false;
      await t.tap(find.byKey(const ValueKey('portfolio-retry')));
      await t.pumpAndSettle();
      expect(find.text('Ines Alvarez'), findsWidgets);
      expect(h.requestsTo('/ines').length, greaterThan(1));
    });

    testWidgets('follow_button_toggles_and_updates_count', (t) async {
      final h = await pumpPortfolio(t, me: meJson());
      expect(find.text('12'), findsOneWidget);
      await t.tap(find.byKey(const ValueKey('portfolio-follow')));
      await t.pumpAndSettle();
      expect(h.requestsTo('/a1/follow'), hasLength(1));
      expect(find.text('Following'), findsWidgets);
      expect(find.text('13'), findsOneWidget);
    });

    testWidgets('guest_follow_requires_sign_in', (t) async {
      final h = await pumpPortfolio(t);
      await t.tap(find.byKey(const ValueKey('portfolio-follow')));
      await t.pumpAndSettle();
      expect(h.signInLaunches, hasLength(1));
      expect(h.requestsTo('/a1/follow'), isEmpty);
    });

    testWidgets('menu_report_artist_goes_through_hook', (t) async {
      final calls = <String>[];
      await pumpPortfolio(
        t,
        me: meJson(),
        overrides: [
          reportSheetHook.overrideWithValue((
            context, {
            required targetType,
            required targetId,
          }) async {
            calls.add('$targetType/$targetId');
            return true;
          }),
        ],
      );
      await t.tap(find.byKey(const ValueKey('portfolio-menu')));
      await t.pumpAndSettle();
      await t.tap(find.text('Report artist'));
      await t.pumpAndSettle();
      expect(calls, ['user/a1']);
    });

    testWidgets('guest_report_requires_sign_in_first', (t) async {
      final calls = <String>[];
      final h = await pumpPortfolio(
        t,
        overrides: [
          reportSheetHook.overrideWithValue((
            context, {
            required targetType,
            required targetId,
          }) async {
            calls.add(targetId);
            return true;
          }),
        ],
      );
      await t.tap(find.byKey(const ValueKey('portfolio-menu')));
      await t.pumpAndSettle();
      await t.tap(find.text('Report artist'));
      await t.pumpAndSettle();
      expect(h.signInLaunches, hasLength(1));
      expect(calls, isEmpty);
    });

    testWidgets('own_profile_has_no_report_menu', (t) async {
      await pumpPortfolio(
        t,
        handle: 'me',
        me: meJson(),
        artist: (_) => jsonResponse(
          200,
          okEnvelope(profileJson(id: 'a9', handle: 'me', isFollowing: null)),
        ),
      );
      expect(find.byKey(const ValueKey('portfolio-menu')), findsNothing);
    });
  });
}
