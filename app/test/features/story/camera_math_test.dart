import 'package:flutter/animation.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:vela_app/features/story/camera_math.dart';

const Size phone = Size(390, 844);

void main() {
  group('computeCamera', () {
    test('centre_pin_zoom_2_4_on_390x844', () {
      final camera = computeCamera(
        x: 0.5,
        y: 0.5,
        zoom: 2.4,
        zoomMultiplier: 1,
        viewport: phone,
      );

      expect(camera.scale, closeTo(2.4, 1e-6));
      expect(camera.tx, closeTo(-273, 1e-6));
      expect(camera.ty, closeTo(-742.72, 1e-6));
      expect(camera.spotlight.dx, closeTo(195, 1e-6));
      expect(camera.spotlight.dy, closeTo(270.08, 1e-6));
    });

    test('pin_at_top_left_clamps_tx_ty_to_zero', () {
      final camera = computeCamera(
        x: 0,
        y: 0,
        zoom: 2.4,
        zoomMultiplier: 1,
        viewport: phone,
      );

      expect(camera.tx, closeTo(0, 1e-6));
      expect(camera.ty, closeTo(0, 1e-6));
      expect(camera.spotlight.dx, closeTo(0, 1e-6));
      expect(camera.spotlight.dy, closeTo(0, 1e-6));
    });

    test('pin_at_bottom_right_clamps_to_w_minus_sw_and_h_minus_sh', () {
      final camera = computeCamera(
        x: 1,
        y: 1,
        zoom: 2.4,
        zoomMultiplier: 1,
        viewport: phone,
      );

      // sW = 936, sH = 2025.6
      expect(camera.tx, closeTo(390 - 936, 1e-6));
      expect(camera.ty, closeTo(844 - 2025.6, 1e-6));
      expect(camera.spotlight.dx, closeTo(390, 1e-6));
      expect(camera.spotlight.dy, closeTo(844, 1e-6));
    });

    test('zoom_multiplier_scales_s', () {
      final camera = computeCamera(
        x: 0.5,
        y: 0.5,
        zoom: 2,
        zoomMultiplier: 1.2,
        viewport: phone,
      );

      expect(camera.scale, closeTo(2.4, 1e-6));
      expect(camera.tx, closeTo(-273, 1e-6));
      expect(camera.ty, closeTo(-742.72, 1e-6));
    });

    test('null_zoom_ignores_zoom_multiplier', () {
      final camera = computeCamera(
        x: 0.5,
        y: 0.5,
        zoom: null,
        zoomMultiplier: 3,
        viewport: phone,
      );

      expect(camera.scale, 1);
      expect(camera.tx, 0);
      expect(camera.ty, 0);
      expect(camera.spotlight.dx, closeTo(195, 1e-6));
      expect(camera.spotlight.dy, closeTo(422, 1e-6));
    });

    test('stay_put_returns_identity_and_spotlight_at_pin', () {
      final camera = computeCamera(
        x: 0.5,
        y: 0.5,
        zoom: null,
        zoomMultiplier: 1,
        viewport: phone,
      );

      expect(camera.scale, 1);
      expect(camera.tx, 0);
      expect(camera.ty, 0);
      expect(camera.spotlight.dx, closeTo(195, 1e-6));
      expect(camera.spotlight.dy, closeTo(422, 1e-6));
    });

    test('scale_is_clamped_to_minimum_of_one_when_zoom_is_below_one', () {
      final camera = computeCamera(
        x: 0.2,
        y: 0.9,
        zoom: 0.5,
        zoomMultiplier: 1,
        viewport: phone,
      );

      expect(camera.scale, 1);
      expect(camera.tx, closeTo(0, 1e-6));
      expect(camera.ty, closeTo(0, 1e-6));
      expect(camera.spotlight.dx, closeTo(78, 1e-6));
      expect(camera.spotlight.dy, closeTo(759.6, 1e-6));
    });

    test('spotlight_stays_inside_viewport_for_every_x_y_in_a_grid_of_0_to_1_step_0_1_with_zoom_1_to_4', () {
      const double w = 390;
      const double h = 844;
      for (var zoom = 1.0; zoom <= 4.0 + 1e-9; zoom += 0.5) {
        for (var i = 0; i <= 10; i++) {
          for (var j = 0; j <= 10; j++) {
            final x = i / 10;
            final y = j / 10;
            final camera = computeCamera(
              x: x,
              y: y,
              zoom: zoom,
              zoomMultiplier: 1,
              viewport: phone,
            );
            final label = 'x=$x y=$y zoom=$zoom';
            expect(camera.spotlight.dx, inInclusiveRange(0, w), reason: label);
            expect(camera.spotlight.dy, inInclusiveRange(0, h), reason: label);
            expect(
              camera.tx,
              inInclusiveRange(w - camera.scale * w, 0),
              reason: label,
            );
            expect(
              camera.ty,
              inInclusiveRange(h - camera.scale * h, 0),
              reason: label,
            );
          }
        }
      }
    });

    test('non_3_4_viewports_work', () {
      for (final viewport in const [Size(360, 640), Size(430, 932)]) {
        final camera = computeCamera(
          x: 0.5,
          y: 0.5,
          zoom: 2.4,
          zoomMultiplier: 1,
          viewport: viewport,
        );
        final w = viewport.width;
        final h = viewport.height;
        const s = 2.4;

        expect(camera.scale, closeTo(s, 1e-6));
        expect(camera.tx, closeTo(w / 2 - s * 0.5 * w, 1e-6));
        expect(camera.ty, closeTo(0.32 * h - s * 0.5 * h, 1e-6));
        expect(camera.spotlight.dx, closeTo(w / 2, 1e-6));
        expect(camera.spotlight.dy, closeTo(0.32 * h, 1e-6));
      }
    });
  });

  group('CameraState.reset', () {
    test('reset_is_identity', () {
      const state = CameraState.reset();

      expect(state.scale, 1);
      expect(state.tx, 0);
      expect(state.ty, 0);
    });
  });

  group('constants', () {
    test('transition_is_900ms_with_brief_curve', () {
      expect(cameraDuration, const Duration(milliseconds: 900));
      expect(cameraCurve, isA<Cubic>());
    });
  });
}
