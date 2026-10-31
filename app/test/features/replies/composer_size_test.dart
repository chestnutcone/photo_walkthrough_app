import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import '../../gallery/support/gallery_fonts.dart';
import 'support/sheet_harness.dart';

/// With the app's real fonts (not the square test font) the composer field
/// was 42pt tall, under the 44/48pt tap target minimum (GALLERY a11y audit).
void main() {
  setUpAll(loadGalleryFonts);

  testWidgets('composer_field_is_at_least_48pt_tall_with_real_fonts', (
    t,
  ) async {
    await pumpSheet(t, replies: []);
    final h = t.getSize(composerInput).height;
    expect(h, greaterThanOrEqualTo(48));
  });
}
