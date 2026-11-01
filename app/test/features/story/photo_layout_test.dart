import 'dart:ui';

import 'package:flutter_test/flutter_test.dart';
import 'package:vela_app/features/story/photo_layout.dart';

const _phone = Size(390, 844);

void main() {
  createFrameTests();
  test('unknown_aspect_fills_the_viewport_with_identity_mapping', () {
    final f = computePhotoFrame(viewport: _phone, aspect: null, pins: const []);
    expect(f.rect, Offset.zero & _phone);
    expect(f.toLayer(const Offset(0.25, 0.75)), const Offset(0.25, 0.75));
  });

  test('portrait_photo_covers_when_all_pins_stay_visible', () {
    // 3:4 photo, pin in the middle: cover crops the sides, pin survives.
    final f = computePhotoFrame(
      viewport: _phone,
      aspect: 3 / 4,
      pins: const [Offset(0.5, 0.5)],
    );
    expect(f.rect.height, closeTo(844, 1e-6));
    expect(f.rect.width, greaterThan(390));
    final p = f.toLayer(const Offset(0.5, 0.5));
    expect(p.dx, closeTo(0.5, 1e-6));
    expect(p.dy, closeTo(0.5, 1e-6));
  });

  test('pin_cropped_out_by_cover_falls_back_to_contain', () {
    final f = computePhotoFrame(
      viewport: _phone,
      aspect: 3 / 4,
      pins: const [Offset(0, 1)],
    );
    expect(f.rect.width, closeTo(390, 1e-6));
    expect(f.rect.height, lessThan(844));
    final p = f.toLayer(const Offset(0, 1));
    expect(p.dx, inInclusiveRange(0, 1));
    expect(p.dy, inInclusiveRange(0, 1));
  });

  test('landscape_photo_is_contained_so_edge_pins_stay_reachable', () {
    final f = computePhotoFrame(
      viewport: _phone,
      aspect: 3 / 2,
      pins: const [Offset(0, 0), Offset(1, 1)],
    );
    expect(f.rect.width, closeTo(390, 1e-6));
    for (final o in const [Offset(0, 0), Offset(1, 1)]) {
      final p = f.toLayer(o);
      expect(p.dx, inInclusiveRange(0, 1));
      expect(p.dy, inInclusiveRange(0, 1));
    }
  });
}

void createFrameTests() {
  group('contain frame (authoring)', () {
    test('toPhotoFraction inverts the pin mapping the viewer uses', () {
      final frame = computeContainFrame(
        viewport: const Size(390, 420),
        aspect: 3 / 4,
      );
      for (final pin in const [Offset(0, 0), Offset(1, 1), Offset(0.3, 0.8)]) {
        final layer = frame.toLayer(pin);
        final point = Offset(layer.dx * 390, layer.dy * 420);
        final back = frame.toPhotoFraction(point)!;
        expect(back.dx, closeTo(pin.dx, 1e-9));
        expect(back.dy, closeTo(pin.dy, 1e-9));
      }
    });

    test('points in the letterbox are not on the photo', () {
      final frame = computeContainFrame(
        viewport: const Size(390, 420),
        aspect: 3 / 4,
      );
      expect(frame.toPhotoFraction(const Offset(2, 200)), isNull);
    });

    test('a very wide photo is letterboxed top and bottom', () {
      final frame = computeContainFrame(
        viewport: const Size(390, 420),
        aspect: 2,
      );
      expect(frame.rect.width, 390);
      expect(frame.rect.height, 195);
      expect(frame.rect.top, closeTo(112.5, 1e-9));
    });
  });
}
