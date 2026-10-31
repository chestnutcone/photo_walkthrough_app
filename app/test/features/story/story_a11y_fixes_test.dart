import 'dart:ui' show SemanticsFlag;

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
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

  // GALLERY a11y audit: the bottom card is a live region, but Android and
  // iOS only announce a live region when its own label changes. The card had
  // no label (its text is in child nodes), so stepping through chapters was
  // silent for TalkBack and VoiceOver users.
  testWidgets('chapter_changes_are_announced_by_the_live_region_label', (
    t,
  ) async {
    final handle = t.ensureSemantics();
    await pumpStory(t);
    String cardLabel() {
      final node = t.getSemantics(find.byKey(const ValueKey('bottom-card')));
      expect(node.hasFlag(SemanticsFlag.isLiveRegion), isTrue);
      return node.label;
    }

    expect(cardLabel(), 'Story intro, 3 chapters');
    await t.tap(find.text('Begin →'));
    await t.pumpAndSettle();
    expect(cardLabel(), 'Chapter 1 of 3, Detail 1');
    await t.tap(find.bySemanticsLabel('Next chapter'));
    await t.pumpAndSettle();
    expect(cardLabel(), 'Chapter 2 of 3, Detail 2');
    await t.tap(find.bySemanticsLabel('Next chapter'));
    await t.pumpAndSettle();
    await t.tap(find.bySemanticsLabel('Next chapter'));
    await t.pumpAndSettle();
    expect(cardLabel(), 'End of story');
    await t.tap(find.text('Explore freely'));
    await t.pumpAndSettle();
    expect(cardLabel(), 'Explore, 3 points');
    handle.dispose();
  });

  // GALLERY web viewer pass: Begin, the tap zones and the pins are gesture
  // detectors, which keyboard focus never reaches, so a keyboard-only reader
  // of a shared story could not leave the intro.
  testWidgets('arrow_keys_step_through_chapters', (t) async {
    await pumpStory(t);
    await t.sendKeyEvent(LogicalKeyboardKey.arrowRight);
    await t.pumpAndSettle();
    expect(kicker(1, 3), findsOneWidget);
    await t.sendKeyEvent(LogicalKeyboardKey.arrowRight);
    await t.pumpAndSettle();
    expect(kicker(2, 3), findsOneWidget);
    await t.sendKeyEvent(LogicalKeyboardKey.arrowLeft);
    await t.pumpAndSettle();
    expect(kicker(1, 3), findsOneWidget);
  });

  // GALLERY a11y audit: the tap zones' gesture detectors added their own
  // unlabelled tap nodes next to the labelled Previous/Next chapter buttons.
  testWidgets('chapter_tap_zones_have_no_unlabelled_tap_nodes', (t) async {
    final handle = t.ensureSemantics();
    await pumpStory(t);
    await t.tap(find.text('Begin →'));
    await t.pumpAndSettle();
    await expectLater(t, meetsGuideline(labeledTapTargetGuideline));
    handle.dispose();
  });
}
