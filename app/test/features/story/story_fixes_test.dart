import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:vela_app/features/story/camera_math.dart';
import 'package:vela_app/features/story/inquiry_sheet_hook.dart';
import 'package:vela_app/features/story/replies_sheet_hook.dart';
import 'package:vela_app/features/story/widgets/spotlight_ring.dart';
import 'package:vela_app/features/story/widgets/story_photo.dart';

import 'support/story_harness.dart';

Finder _badge(int i) => find.descendant(
  of: pin(i),
  matching: find.byKey(const ValueKey('pin-badge')),
);

Offset _ring(WidgetTester t) =>
    t.widget<SpotlightRing>(find.byType(SpotlightRing)).center;

Future<void> _begin(WidgetTester t) async {
  await t.tap(find.text('Begin →'));
  await t.pumpAndSettle();
}

void main() {
  test('camera_state_has_value_equality', () {
    const a = CameraState(scale: 2, tx: -1, ty: -2, spotlight: Offset(3, 4));
    const b = CameraState(scale: 2, tx: -1, ty: -2, spotlight: Offset(3, 4));
    expect(a, b);
    expect(a.hashCode, b.hashCode);
    expect(a, isNot(const CameraState.reset()));
  });

  group('tween restarts', () {
    testWidgets('rebuild_mid_flight_does_not_restart_the_camera_tween', (
      t,
    ) async {
      await pumpStory(t);
      await t.tap(find.text('Begin →'));
      await t.pump();
      await t.pump(const Duration(milliseconds: 300));
      await t.tap(find.text('Reply')); // flips threadOpen -> rebuild
      await t.pump();
      await t.pump(const Duration(milliseconds: 600));
      final m = t.widget<Transform>(find.byKey(const ValueKey('photo-layer')));
      expect(m.transform.storage[0], closeTo(2.4, 1e-6));
    });
  });

  group('hooks', () {
    testWidgets('replies_hook_receives_read_only_flag', (t) async {
      final flags = <bool>[];
      await pumpStory(
        t,
        readOnly: true,
        overrides: [
          openRepliesSheetProvider.overrideWithValue(
            (ctx, w, i, {required readOnly}) async => flags.add(readOnly),
          ),
        ],
      );
      await _begin(t);
      await t.tap(find.text('Reply'));
      await t.pumpAndSettle();
      expect(flags, [true]);
    });

    testWidgets('throwing_replies_hook_is_caught_and_thread_closes', (t) async {
      await pumpStory(
        t,
        overrides: [
          openRepliesSheetProvider.overrideWithValue(
            (ctx, w, i, {required readOnly}) async => throw StateError('boom'),
          ),
        ],
      );
      await _begin(t);
      await t.tap(find.text('Reply'));
      await t.pumpAndSettle();
      expect(t.takeException(), isNull);
      expect(find.text('Reply'), findsOneWidget);
    });

    test('inquiry_hook_type_is_optional', () {
      Future<void> f(
        BuildContext c, {
        required String artistId,
        required String workId,
        String? type,
      }) async {}
      final OpenInquirySheet h = f;
      expect(h, isNotNull);
    });
  });

  group('edge pins and the bottom card', () {
    testWidgets('edge_pin_hit_rect_does_not_intersect_the_bottom_card', (
      t,
    ) async {
      await pumpStory(
        t,
        work: workJson(
          chapters: [
            chapterJson(0, x: 0, y: 1),
            chapterJson(1, x: 1, y: 1),
            chapterJson(2, x: 0.5, y: 0.9),
          ],
        ),
      );
      final card = t.getRect(find.byKey(const ValueKey('bottom-card')));
      for (var i = 0; i < 3; i++) {
        final hit = t.getRect(pin(i));
        expect(hit.size, const Size(44, 44));
        expect(
          hit.overlaps(card),
          isFalse,
          reason: 'pin $i $hit vs card $card',
        );
        expect(
          const Rect.fromLTWH(0, 0, 390, 844).contains(hit.center),
          isTrue,
        );
      }
    });

    testWidgets('edge_pin_hit_rect_clear_of_card_in_explore_zoom', (t) async {
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
      final card = t.getRect(find.byKey(const ValueKey('bottom-card')));
      expect(t.getRect(pin(0)).overlaps(card), isFalse);
    });
  });

  group('non-3:4 photos: ring centre equals pin badge centre', () {
    final cases = <String, (int, int, double, double)>{
      'landscape_contain': (3000, 2000, 0.5, 0.4),
      'portrait_cover': (3000, 4000, 0.5, 0.4),
      'portrait_contain_edge_pin': (3000, 4000, 0.05, 0.4),
      'square_cover': (2000, 2000, 0.5, 0.4),
      'square_contain_edge_pin': (2000, 2000, 0.1, 0.4),
    };
    for (final e in cases.entries) {
      for (final zoom in <double?>[2.4, null]) {
        testWidgets('${e.key}_${zoom == null ? 'stay_put' : 'zoom'}', (
          t,
        ) async {
          final (w, h, x, y) = e.value;
          await pumpStory(
            t,
            work: workJson(
              imageWidth: w,
              imageHeight: h,
              chapters: [chapterJson(0, x: x, y: y, zoom: zoom)],
            ),
          );
          await _begin(t);
          final ring = _ring(t);
          final badge = t.getCenter(_badge(0));
          // Edge pins are pulled inward by up to 22pt (documented tradeoff);
          // these edge cases sit 2-3pt from the edge.
          final tol = e.key.contains('edge') ? 3.0 : 0.5;
          expect(ring.dx, closeTo(badge.dx, tol));
          expect(ring.dy, closeTo(badge.dy, tol));
          expect(const Rect.fromLTWH(0, 0, 390, 844).contains(ring), isTrue);
        });
      }
    }

    testWidgets('cover_vs_contain_is_chosen_per_work', (t) async {
      await pumpStory(
        t,
        work: workJson(
          imageWidth: 3000,
          imageHeight: 4000,
          chapters: [chapterJson(0, x: 0.5, y: 0.4)],
        ),
      );
      expect(
        t.widget<StoryPhoto>(find.byType(StoryPhoto)).rect.height,
        closeTo(844, 0.01),
      );
    });

    testWidgets('edge_pin_makes_the_whole_work_contain', (t) async {
      await pumpStory(
        t,
        work: workJson(
          imageWidth: 3000,
          imageHeight: 4000,
          chapters: [
            chapterJson(0, x: 0.05, y: 0.4),
            chapterJson(1, x: 0.5, y: 0.5),
          ],
        ),
      );
      expect(
        t.widget<StoryPhoto>(find.byType(StoryPhoto)).rect.width,
        closeTo(390, 0.01),
      );
    });
  });

  group('layout deltas', () {
    testWidgets('top_bar_rows_match_prototype_y', (t) async {
      await pumpStory(t);
      // Story mode: segments at 52, row at 67.
      expect(
        t.getTopLeft(find.byKey(const ValueKey('close'))).dy,
        closeTo(67 + 0, 4),
      );
      final seg = t.getRect(find.byKey(const ValueKey('segment-0')));
      expect(seg.top + 6, closeTo(52, 0.01));
      await t.tap(find.text('Explore'));
      await t.pumpAndSettle();
      // Explore: row at 52 (36pt close button top edge).
      expect(
        t.getTopLeft(find.byKey(const ValueKey('close'))).dy,
        closeTo(52, 4),
      );
    });

    testWidgets('bottom_card_sits_24_from_bottom', (t) async {
      await pumpStory(t);
      expect(
        t.getRect(find.byKey(const ValueKey('bottom-card'))).bottom,
        closeTo(820, 0.01),
      );
    });

    testWidgets('bottom_card_keeps_24_with_34pt_safe_area', (t) async {
      t.view.padding = const FakeViewPadding(bottom: 34);
      t.view.viewPadding = const FakeViewPadding(bottom: 34);
      await pumpStory(t);
      expect(
        t.getRect(find.byKey(const ValueKey('bottom-card'))).bottom,
        closeTo(820, 0.01),
      );
    });

    testWidgets('photo_error_retry_is_tappable_in_a_chapter_step', (t) async {
      var loads = 0;
      await pumpStory(
        t,
        imageFactory: (u) {
          loads++;
          return FailingImage(u);
        },
      );
      await _begin(t);
      expect(kicker(1, 3), findsOneWidget);
      final before = loads;
      await t.tap(find.text('Retry'));
      await t.pumpAndSettle();
      expect(kicker(1, 3), findsOneWidget, reason: 'tap must not advance');
      expect(loads, greaterThan(before));
    });

    testWidgets('reduced_motion_zeroes_ring_pin_progress_and_photo_fades', (
      t,
    ) async {
      await pumpStory(t);
      t.platformDispatcher.accessibilityFeaturesTestValue =
          const FakeAccessibilityFeatures(disableAnimations: true);
      addTearDown(t.platformDispatcher.clearAccessibilityFeaturesTestValue);
      await t.pump();
      final fades = t.widgetList<AnimatedOpacity>(find.byType(AnimatedOpacity));
      expect(fades, isNotEmpty);
      for (final f in fades) {
        expect(f.duration, Duration.zero);
      }
      final seg = t.widget<AnimatedFractionallySizedBox>(
        find.byType(AnimatedFractionallySizedBox).first,
      );
      expect(seg.duration, Duration.zero);
    });
  });

  group('unavailable vs failed', () {
    for (final status in [401, 403, 404, 410]) {
      testWidgets('status_${status}_is_unavailable_without_retry', (t) async {
        await pumpStory(t, workStatus: status, errorCode: 'x');
        expect(find.text("This story isn't available"), findsOneWidget);
        expect(find.text('Retry'), findsNothing);
      });
    }
    for (final status in [429, 500, 503]) {
      testWidgets('status_${status}_offers_retry', (t) async {
        await pumpStory(t, workStatus: status, errorCode: 'x');
        expect(find.text("Couldn't load this story"), findsOneWidget);
        expect(find.text('Retry'), findsOneWidget);
      });
    }
    testWidgets('network_error_offers_retry', (t) async {
      await pumpStory(t, networkError: true);
      expect(find.text("Couldn't load this story"), findsOneWidget);
      expect(find.text('Retry'), findsOneWidget);
    });
  });
}
