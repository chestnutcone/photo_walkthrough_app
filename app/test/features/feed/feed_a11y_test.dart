import 'package:flutter_test/flutter_test.dart';

import '../../core/support/fakes.dart';
import 'support/feed_harness.dart';

/// E1 audit (docs/review/a11y-violations.txt): every control on the feed,
/// guest and signed in, meets Android's 48pt tap target guideline (S2-6).
void main() {
  for (final signedIn in [false, true]) {
    testWidgets('feed_meets_the_tap_target_guideline_signed_in_$signedIn', (
      t,
    ) async {
      final handle = t.ensureSemantics();
      await pumpFeed(
        t,
        signedIn: signedIn,
        unread: 1,
        feed: (_) => jsonResponse(200, feedPage([feedWork('w1')])),
      );

      await expectLater(t, meetsGuideline(androidTapTargetGuideline));
      await expectLater(t, meetsGuideline(labeledTapTargetGuideline));
      handle.dispose();
    });
  }
}
