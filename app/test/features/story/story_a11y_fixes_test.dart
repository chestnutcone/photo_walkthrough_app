import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'support/story_harness.dart';

/// E1 accessibility and navigation fixes in the story viewer (S2-6).
void main() {
  testWidgets('system_back_from_a_deep_linked_story_goes_to_the_feed', (
    t,
  ) async {
    final h = await pumpStory(t);
    expect(h.router.canPop(), isFalse, reason: 'opened with no history');

    await t.binding.handlePopRoute();
    await t.pumpAndSettle();

    expect(find.text('route:feed'), findsOneWidget);
  });

  for (final (width, scale) in [(320.0, 1.0), (390.0, 2.0), (320.0, 2.0)]) {
    testWidgets('artist_name_stays_visible_at_${width}pt_scale_$scale', (
      t,
    ) async {
      await pumpStory(t);
      t.view.physicalSize = Size(width, 640);
      t.platformDispatcher.textScaleFactorTestValue = scale;
      addTearDown(t.platformDispatcher.clearTextScaleFactorTestValue);
      await t.pumpAndSettle();

      final name = find.text('Ines Alvarez');
      expect(name, findsOneWidget);
      final rect = t.getRect(name);
      expect(rect.width, greaterThan(40), reason: 'name has room: $rect');
      expect(rect.right, lessThanOrEqualTo(width));
    });
  }

  // The 3pt progress segments keep their 15pt band (ruling S2-6: a 48pt band
  // would overlap the 48pt row below; tap zones, pins and the Previous/Next
  // chapter buttons do the same at 48pt). Everything else is checked here.
  testWidgets('story_chrome_tap_targets_are_at_least_48pt', (t) async {
    final handle = t.ensureSemantics();
    await pumpStory(t);
    for (final label in [
      'Story',
      'Explore',
      'Close',
      'Open Ines Alvarez portfolio',
    ]) {
      final size = t.getSemantics(find.bySemanticsLabel(label)).rect.size;
      expect(size.width, greaterThanOrEqualTo(48), reason: label);
      expect(size.height, greaterThanOrEqualTo(48), reason: label);
    }
    handle.dispose();
  });
}
