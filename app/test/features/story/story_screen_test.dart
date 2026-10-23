import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:vela_app/features/story/camera_math.dart';
import 'package:vela_app/features/story/inquiry_sheet_hook.dart';
import 'package:vela_app/features/story/replies_sheet_hook.dart';
import 'package:vela_app/features/story/widgets/spotlight_ring.dart';

import 'support/story_harness.dart';

Offset _ringCenter(WidgetTester t) =>
    t.widget<SpotlightRing>(find.byType(SpotlightRing)).center;

Matrix4 _photoTransform(WidgetTester t) =>
    t.widget<Transform>(find.byKey(const ValueKey('photo-layer'))).transform;

bool _within(Rect outer, Rect inner, {double eps = 0.01}) =>
    inner.left >= outer.left - eps &&
    inner.top >= outer.top - eps &&
    inner.right <= outer.right + eps &&
    inner.bottom <= outer.bottom + eps;

Future<void> _begin(WidgetTester t) async {
  await t.tap(find.text('Begin →'));
  await t.pumpAndSettle();
}

void main() {
  group('layout states', () {
    testWidgets('intro_shows_kicker_title_intro_and_begin', (t) async {
      await pumpStory(t);
      expect(find.text('OCT 2026 · 3 CHAPTERS'), findsOneWidget);
      expect(find.text('Salt and Rope'), findsWidgets);
      expect(find.text('A morning on the quay.'), findsOneWidget);
      expect(find.text('Begin →'), findsOneWidget);
    });

    testWidgets('chapter_card_shows_kicker_title_body_meta', (t) async {
      await pumpStory(t);
      await _begin(t);
      expect(kicker(1, 3), findsOneWidget);
      expect(find.text('Detail 1'), findsOneWidget);
      expect(find.text('Body of detail 1'), findsOneWidget);
      expect(find.text('85mm · f/4'), findsOneWidget);
      expect(find.text('Reply'), findsOneWidget);
    });

    testWidgets('reply_pill_shows_count', (t) async {
      await pumpStory(t);
      await _begin(t);
      await t.tapAt(const Offset(300, 300));
      await t.pumpAndSettle();
      expect(find.text('2 replies'), findsOneWidget);
    });

    testWidgets('explore_idle_card_counts_notes_by_first_name', (t) async {
      await pumpStory(t);
      await t.tap(find.text('Explore'));
      await t.pumpAndSettle();
      expect(
        find.text('Tap any marked point in any order. 3 notes left by Ines.'),
        findsOneWidget,
      );
      expect(find.text('EXPLORE'), findsOneWidget);
    });
  });

  group('camera', () {
    testWidgets('spotlight_ring_positioned_at_camera_spotlight_point', (
      t,
    ) async {
      await pumpStory(t);
      await _begin(t);
      final expected = computeCamera(
        x: 0.3,
        y: 0.4,
        zoom: 2.4,
        zoomMultiplier: 1,
        viewport: phone,
      );
      final c = _ringCenter(t);
      expect(c.dx, closeTo(expected.spotlight.dx, 0.01));
      expect(c.dy, closeTo(expected.spotlight.dy, 0.01));
      final m = _photoTransform(t);
      expect(m.storage[0], closeTo(2.4, 1e-6));
      expect(m.storage[12], closeTo(expected.tx, 0.01));
      expect(m.storage[13], closeTo(expected.ty, 0.01));
    });

    testWidgets('stay_put_chapter_shows_unzoomed_photo_with_ring_on_pin', (
      t,
    ) async {
      await pumpStory(t);
      await _begin(t);
      await t.tapAt(const Offset(300, 300)); // chapter 2 has no zoom
      await t.pumpAndSettle();
      expect(kicker(2, 3), findsOneWidget);
      final m = _photoTransform(t);
      expect(m.storage[0], 1);
      expect(m.storage[12], 0);
      expect(m.storage[13], 0);
      final c = _ringCenter(t);
      expect(c.dx, closeTo(0.6 * 390, 0.01));
      expect(c.dy, closeTo(0.5 * 844, 0.01));
    });

    testWidgets('camera_animates_with_the_900ms_duration', (t) async {
      await pumpStory(t);
      await t.tap(find.text('Begin →'));
      await t.pump();
      await t.pump(const Duration(milliseconds: 450));
      final mid = _photoTransform(t).storage[0];
      expect(mid, greaterThan(1));
      expect(mid, lessThan(2.4));
      await t.pump(const Duration(milliseconds: 600));
      expect(_photoTransform(t).storage[0], closeTo(2.4, 1e-6));
    });

    testWidgets('reduced_motion_jumps_without_animation', (t) async {
      await pumpStory(t);
      t.platformDispatcher.accessibilityFeaturesTestValue =
          const FakeAccessibilityFeatures(disableAnimations: true);
      addTearDown(t.platformDispatcher.clearAccessibilityFeaturesTestValue);
      await t.pump();
      await t.tap(find.text('Begin →'));
      await t.pump();
      await t.pump(const Duration(milliseconds: 20));
      expect(_photoTransform(t).storage[0], closeTo(2.4, 1e-6));
    });
  });

  group('pins', () {
    testWidgets('pins_hidden_during_chapters_visible_on_intro_and_explore', (
      t,
    ) async {
      await pumpStory(t);
      double opacity() => t
          .widget<AnimatedOpacity>(find.byKey(const ValueKey('pin-layer')))
          .opacity;
      expect(opacity(), 1);
      await _begin(t);
      expect(opacity(), 0);
      await t.tap(find.text('Explore'));
      await t.pumpAndSettle();
      expect(opacity(), 1);
      await t.tap(find.text('Story'));
      await t.pumpAndSettle();
      expect(opacity(), 1);
    });

    testWidgets('intro_pin_tap_jumps_to_that_chapter', (t) async {
      await pumpStory(t);
      await t.tap(pin(1));
      await t.pumpAndSettle();
      expect(kicker(2, 3), findsOneWidget);
    });

    testWidgets('explore_pin_tap_zooms_and_zoom_out_resets', (t) async {
      await pumpStory(t);
      await t.tap(find.text('Explore'));
      await t.pumpAndSettle();
      await t.tap(pin(0));
      await t.pumpAndSettle();
      expect(kicker(1, 3), findsOneWidget);
      expect(_photoTransform(t).storage[0], closeTo(2.4, 1e-6));
      await t.tap(find.text('Zoom out'));
      await t.pumpAndSettle();
      expect(_photoTransform(t).storage[0], 1);
      expect(find.text('EXPLORE'), findsOneWidget);
    });

    testWidgets('explore_background_tap_zooms_out', (t) async {
      await pumpStory(t);
      await t.tap(find.text('Explore'));
      await t.pumpAndSettle();
      await t.tap(pin(0));
      await t.pumpAndSettle();
      await t.tapAt(const Offset(200, 150));
      await t.pumpAndSettle();
      expect(_photoTransform(t).storage[0], 1);
    });

    testWidgets('pins_are_counter_scaled_to_constant_size', (t) async {
      await pumpStory(t);
      await t.tap(find.text('Explore'));
      await t.pumpAndSettle();
      await t.tap(pin(0));
      await t.pumpAndSettle();
      final badge = find.descendant(
        of: pin(1),
        matching: find.byKey(const ValueKey('pin-badge')),
      );
      final size = t.getSize(badge);
      expect(size.width, closeTo(28, 0.01));
      expect(size.height, closeTo(28, 0.01));
    });

    testWidgets('edge_pins_x0_y1_render_inside_screen', (t) async {
      await pumpStory(
        t,
        work: workJson(
          chapters: [
            chapterJson(0, x: 0, y: 1),
            chapterJson(1, x: 1, y: 0),
            chapterJson(2, x: 1, y: 1),
            chapterJson(3, x: 0, y: 0),
          ],
        ),
      );
      const screen = Rect.fromLTWH(0, 0, 390, 844);
      for (var i = 0; i < 4; i++) {
        final r = t.getRect(
          find.descendant(
            of: pin(i),
            matching: find.byKey(const ValueKey('pin-badge')),
          ),
        );
        expect(_within(screen, r), isTrue, reason: 'badge $i $r');
        expect(t.getRect(pin(i)).size, const Size(44, 44));
        expect(_within(screen, t.getRect(pin(i))), isTrue, reason: 'hit $i');
      }
    });

    testWidgets('edge_pins_stay_inside_when_zoomed_in_explore', (t) async {
      await pumpStory(
        t,
        work: workJson(
          chapters: [
            chapterJson(0, x: 0, y: 1),
            chapterJson(1, x: 0.5, y: 0.5),
          ],
        ),
      );
      await t.tap(find.text('Explore'));
      await t.pumpAndSettle();
      await t.tap(pin(0));
      await t.pumpAndSettle();
      expect(
        _within(const Rect.fromLTWH(0, 0, 390, 844), t.getRect(pin(0))),
        isTrue,
      );
    });

    testWidgets('non_3_4_photo_keeps_spotlight_on_screen', (t) async {
      await pumpStory(
        t,
        work: workJson(
          imageWidth: 3000,
          imageHeight: 2000,
          chapters: [chapterJson(0, x: 0, y: 1, zoom: null)],
        ),
      );
      await _begin(t);
      final c = _ringCenter(t);
      expect(const Rect.fromLTWH(0, 0, 390, 844).contains(c), isTrue);
    });
  });

  group('navigation', () {
    testWidgets('tapping_progress_segment_jumps', (t) async {
      await pumpStory(t);
      await t.tap(find.byKey(const ValueKey('segment-2')));
      await t.pumpAndSettle();
      expect(kicker(3, 3), findsOneWidget);
      await t.tap(find.byKey(const ValueKey('segment-0')));
      await t.pumpAndSettle();
      expect(kicker(1, 3), findsOneWidget);
    });

    testWidgets('progress_segments_hidden_in_explore', (t) async {
      await pumpStory(t);
      expect(find.byKey(const ValueKey('segment-0')), findsOneWidget);
      await t.tap(find.text('Explore'));
      await t.pumpAndSettle();
      expect(find.byKey(const ValueKey('segment-0')), findsNothing);
    });

    testWidgets('tap_zones_left_30_prev_right_70_next', (t) async {
      await pumpStory(t);
      await _begin(t);
      await t.tapAt(const Offset(300, 300));
      await t.pumpAndSettle();
      expect(kicker(2, 3), findsOneWidget);
      await t.tapAt(const Offset(100, 300));
      await t.pumpAndSettle();
      expect(kicker(1, 3), findsOneWidget);
      // Just inside the left zone boundary (30% of 390 = 117).
      await t.tapAt(const Offset(116, 300));
      await t.pumpAndSettle();
      expect(find.text('Begin →'), findsOneWidget);
    });

    testWidgets('tap_zones_do_not_reach_outside_y_110_to_510', (t) async {
      await pumpStory(t);
      await _begin(t);
      await t.tapAt(const Offset(300, 520));
      await t.pumpAndSettle();
      expect(kicker(1, 3), findsOneWidget);
    });

    testWidgets(
      'bottom_card_is_a_live_region_so_chapter_changes_are_announced',
      (t) async {
        final handle = t.ensureSemantics();
        await pumpStory(t);
        await _begin(t);
        final node = t.getSemantics(find.byKey(const ValueKey('bottom-card')));
        expect(node.flagsCollection.isLiveRegion, isTrue);
        handle.dispose();
      },
    );

    testWidgets('close_goes_home_for_a_visitor', (t) async {
      await pumpStory(t);
      await t.tap(find.byKey(const ValueKey('close')));
      await t.pumpAndSettle();
      expect(find.text('route:feed'), findsOneWidget);
    });

    testWidgets('close_goes_to_own_portfolio_for_owner', (t) async {
      await pumpStory(t, work: workJson(isOwner: true, isFollowing: null));
      await t.tap(find.byKey(const ValueKey('close')));
      await t.pumpAndSettle();
      expect(find.text('route:artist/ines'), findsOneWidget);
    });

    testWidgets('artist_header_opens_portfolio', (t) async {
      await pumpStory(t);
      await t.tap(find.text('Ines Alvarez'));
      await t.pumpAndSettle();
      expect(find.text('route:artist/ines'), findsOneWidget);
    });
  });

  group('end card', () {
    Future<void> toEnd(WidgetTester t) async {
      await t.tap(find.byKey(const ValueKey('segment-2')));
      await t.pumpAndSettle();
      await t.tapAt(const Offset(300, 300));
      await t.pumpAndSettle();
    }

    testWidgets(
      'end_card_shows_follow_and_next_story_and_print_inquiry_buttons',
      (t) async {
        await pumpStory(t);
        await toEnd(t);
        expect(find.text('END OF STORY'), findsOneWidget);
        expect(find.text('Keep looking, or move on.'), findsOneWidget);
        expect(find.text('Explore freely'), findsOneWidget);
        expect(find.text('Replay'), findsOneWidget);
        expect(find.text('Follow Ines'), findsOneWidget);
        expect(find.text('Next story →'), findsOneWidget);
        expect(find.text('Ask about a print of this work'), findsOneWidget);
      },
    );

    testWidgets('replay_returns_to_intro', (t) async {
      await pumpStory(t);
      await toEnd(t);
      await t.tap(find.text('Replay'));
      await t.pumpAndSettle();
      expect(find.text('Begin →'), findsOneWidget);
    });

    testWidgets('follow_calls_api_and_flips_label', (t) async {
      final h = await pumpStory(t, signedIn: true);
      await toEnd(t);
      await t.tap(find.text('Follow Ines'));
      await t.pumpAndSettle();
      expect(h.adapter.requests.last.method, 'PUT');
      expect(h.adapter.requests.last.path, '/v1/artists/a1/follow');
      expect(find.text('Following Ines'), findsOneWidget);
      await t.tap(find.text('Following Ines'));
      await t.pumpAndSettle();
      expect(h.adapter.requests.last.method, 'DELETE');
      expect(find.text('Follow Ines'), findsOneWidget);
    });

    testWidgets('guest_follow_requires_sign_in_and_makes_no_call', (t) async {
      final h = await pumpStory(t);
      await toEnd(t);
      await t.tap(find.text('Follow Ines'));
      await t.pumpAndSettle();
      expect(h.adapter.count('/v1/artists/a1/follow'), 0);
      expect(find.text('Follow Ines'), findsOneWidget);
    });

    testWidgets('guest_follow_proceeds_after_successful_sign_in', (t) async {
      final h = await pumpStory(t, signInSucceeds: true);
      await toEnd(t);
      await t.tap(find.text('Follow Ines'));
      await t.pumpAndSettle();
      expect(h.adapter.count('/v1/artists/a1/follow'), 1);
      expect(find.text('Following Ines'), findsOneWidget);
    });

    testWidgets('follow_failure_rolls_back_and_shows_message', (t) async {
      final h = await pumpStory(t, signedIn: true, followStatus: 500);
      await toEnd(t);
      await t.tap(find.text('Follow Ines'));
      await t.pumpAndSettle();
      expect(h.adapter.count('/v1/artists/a1/follow'), 1);
      expect(find.text('Follow Ines'), findsOneWidget);
      expect(find.textContaining("Couldn't"), findsOneWidget);
    });

    testWidgets('next_story_goes_to_feed', (t) async {
      await pumpStory(t);
      await toEnd(t);
      await t.tap(find.text('Next story →'));
      await t.pumpAndSettle();
      expect(find.text('route:feed'), findsOneWidget);
    });

    testWidgets('inquiry_button_calls_inquiry_hook', (t) async {
      final calls = <String>[];
      await pumpStory(
        t,
        overrides: [
          openInquirySheetProvider.overrideWithValue(
            (context, {required artistId, required workId}) async =>
                calls.add('$artistId/$workId'),
          ),
        ],
      );
      await toEnd(t);
      await t.tap(find.text('Ask about a print of this work'));
      await t.pumpAndSettle();
      expect(calls, ['a1/w1']);
    });

    testWidgets('owner_sees_portfolio_button_and_no_inquiry', (t) async {
      await pumpStory(t, work: workJson(isOwner: true, isFollowing: null));
      await toEnd(t);
      expect(find.text('Your portfolio'), findsOneWidget);
      expect(find.text('Ask about a print of this work'), findsNothing);
    });

    testWidgets('read_only_hides_follow_and_inquiry_but_keeps_navigation', (
      t,
    ) async {
      await pumpStory(t, readOnly: true);
      await toEnd(t);
      expect(find.text('Follow Ines'), findsNothing);
      expect(find.text('Ask about a print of this work'), findsNothing);
      expect(find.text('Explore freely'), findsOneWidget);
      expect(find.text('Replay'), findsOneWidget);
      expect(find.text('Next story →'), findsOneWidget);
    });
  });

  group('replies and autoplay', () {
    testWidgets('read_only_keeps_replies_entry', (t) async {
      await pumpStory(t, readOnly: true);
      await _begin(t);
      expect(find.text('Reply'), findsOneWidget);
    });

    testWidgets('replies_pill_calls_hook_with_work_and_chapter_index', (
      t,
    ) async {
      final calls = <String>[];
      await pumpStory(
        t,
        overrides: [
          openRepliesSheetProvider.overrideWithValue((ctx, workId, i) async {
            calls.add('$workId/$i');
          }),
        ],
      );
      await _begin(t);
      await t.tap(find.text('Reply'));
      await t.pumpAndSettle();
      expect(calls, ['w1/0']);
    });

    testWidgets('autoplay_advances_every_6s_and_pauses_when_replies_open', (
      t,
    ) async {
      await pumpStory(t, autoplay: true);
      await _begin(t);
      expect(kicker(1, 3), findsOneWidget);
      await t.pump(const Duration(seconds: 6));
      expect(kicker(2, 3), findsOneWidget);

      // Default stub keeps the thread flagged open until the pill is tapped
      // again; autoplay must not advance meanwhile.
      await t.tap(find.text('2 replies'));
      await t.pump();
      await t.pump(const Duration(seconds: 20));
      expect(kicker(2, 3), findsOneWidget);

      await t.tap(find.text('2 replies'));
      await t.pump();
      await t.pump(const Duration(seconds: 6));
      expect(kicker(3, 3), findsOneWidget);
      await t.pump(const Duration(seconds: 6));
      expect(find.text('END OF STORY'), findsOneWidget);
    });
  });

  group('photo', () {
    testWidgets('alt_text_is_exposed_as_semantics_label', (t) async {
      final handle = t.ensureSemantics();
      await pumpStory(t);
      expect(find.bySemanticsLabel('A lighthouse at dusk'), findsOneWidget);
      handle.dispose();
    });

    testWidgets('missing_alt_text_falls_back_to_title', (t) async {
      final handle = t.ensureSemantics();
      await pumpStory(t, work: workJson(altText: null));
      expect(
        find.bySemanticsLabel(RegExp('^Photograph: Salt and Rope')),
        findsOneWidget,
      );
      handle.dispose();
    });

    testWidgets('image_error_shows_retry_that_reloads', (t) async {
      var attempts = 0;
      await pumpStory(
        t,
        imageFactory: (url) {
          attempts++;
          return FailingImage(url);
        },
      );
      expect(find.text("Couldn't load this photo"), findsOneWidget);
      final before = attempts;
      await t.tap(find.text('Retry'));
      await t.pumpAndSettle();
      expect(attempts, greaterThan(before));
    });
  });

  group('load states', () {
    testWidgets('shows_progress_while_loading', (t) async {
      await pumpStory(t, settle: false);
      expect(find.byType(CircularProgressIndicator), findsOneWidget);
      await t.pumpAndSettle();
    });

    for (final status in [404, 403, 410]) {
      testWidgets('status_${status}_shows_same_unavailable_message', (t) async {
        await pumpStory(t, workStatus: status, errorCode: 'whatever');
        expect(find.text("This story isn't available"), findsOneWidget);
        expect(find.text('Retry'), findsNothing);
      });
    }

    testWidgets('server_error_shows_retry_that_refetches', (t) async {
      final h = await pumpStory(t, workStatus: 500, errorCode: 'server');
      expect(find.text("Couldn't load this story"), findsOneWidget);
      await t.tap(find.text('Retry'));
      await t.pumpAndSettle();
      expect(h.adapter.count('/v1/works/w1'), 2);
    });

    testWidgets('empty_story_has_no_progress_and_ends_after_intro', (t) async {
      await pumpStory(t, work: workJson(chapters: []));
      expect(find.byKey(const ValueKey('segment-0')), findsNothing);
      expect(find.text('OCT 2026 · 0 CHAPTERS'), findsOneWidget);
    });
  });
}
