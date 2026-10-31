import 'package:flutter_test/flutter_test.dart';

import 'portfolio/support/portfolio_harness.dart';
import 'search/support/search_harness.dart';

/// E1 audit screens beyond the feed and story (docs/review/a11y-triage.md):
/// every control meets Android's 48pt tap target guideline (S2-6).
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
}
