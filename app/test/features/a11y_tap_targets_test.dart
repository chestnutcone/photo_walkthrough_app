import 'package:flutter_test/flutter_test.dart';

import 'notifications/support/fake_notifications_api.dart';
import 'notifications/support/notifications_harness.dart';
import 'portfolio/support/portfolio_harness.dart';
import 'profile_edit/support/edit_harness.dart';
import 'search/support/search_harness.dart';

/// E1 audit screens beyond the feed and story (docs/review/a11y-triage.md):
/// every control meets Android's 48pt tap target guideline (S2-6; the
/// notifications and settings screens added in the S2 fix, review M-15).
void main() {
  testWidgets('search_suggestions_meet_the_tap_target_guideline', (t) async {
    final handle = t.ensureSemantics();
    await pumpSearch(t);
    await expectLater(t, meetsGuideline(androidTapTargetGuideline));
    handle.dispose();
  });

  testWidgets('portfolio_meets_the_tap_target_guideline', (t) async {
    final handle = t.ensureSemantics();
    await pumpPortfolio(t);
    await expectLater(t, meetsGuideline(androidTapTargetGuideline));
    handle.dispose();
  });

  testWidgets('notifications_meet_the_tap_target_guideline', (t) async {
    final handle = t.ensureSemantics();
    final h = await pumpNotifications(t);
    await answerList(t, h.api, [
      notifJson('n1', type: 'follow', workId: null),
      notifJson('n2', type: 'new_story', actorId: 'a2', read: true),
      notifJson('n3', count: 3, ids: ['n3', 'n2', 'n1']),
    ]);
    await expectLater(t, meetsGuideline(androidTapTargetGuideline));
    await expectLater(t, meetsGuideline(labeledTapTargetGuideline));
    handle.dispose();
  });

  testWidgets('settings_meet_the_tap_target_guideline', (t) async {
    final handle = t.ensureSemantics();
    await pumpEdit(t, path: '/settings');
    await expectLater(t, meetsGuideline(androidTapTargetGuideline));
    await expectLater(t, meetsGuideline(labeledTapTargetGuideline));
    handle.dispose();
  });

  testWidgets('settings_text_meets_the_contrast_guideline', (t) async {
    final handle = t.ensureSemantics();
    await pumpEdit(t, path: '/settings');
    await expectLater(t, meetsGuideline(textContrastGuideline));
    handle.dispose();
  });
}
