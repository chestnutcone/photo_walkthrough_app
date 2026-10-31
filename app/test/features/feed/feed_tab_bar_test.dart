import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import '../../core/support/fakes.dart';
import 'support/feed_harness.dart';

ResponseBody _feed(RequestOptions o) =>
    jsonResponse(200, feedPage([feedWork('w1')]));

void main() {
  testWidgets('guest_plus_asks_to_sign_in_and_does_not_navigate', (
    tester,
  ) async {
    final h = await pumpFeed(tester, feed: _feed);
    expect(find.byKey(const ValueKey('tab-bar-create')), findsOneWidget);
    await tester.tap(find.byKey(const ValueKey('tab-bar-create')));
    await tester.pumpAndSettle();
    expect(h.signInLaunches, hasLength(1));
    expect(find.text('route:create'), findsNothing);
  });

  testWidgets('signed_in_plus_opens_the_story_editor', (tester) async {
    final h = await pumpFeed(tester, signedIn: true, feed: _feed);
    await tester.tap(find.byKey(const ValueKey('tab-bar-create')));
    await tester.pumpAndSettle();
    expect(h.signInLaunches, isEmpty);
    expect(find.text('route:create'), findsOneWidget);
  });

  testWidgets('bar_is_labelled_and_tap_targets_are_large_enough', (
    tester,
  ) async {
    await pumpFeed(tester, signedIn: true, feed: _feed);
    final semantics = tester.ensureSemantics();
    expect(find.bySemanticsLabel('New story'), findsOneWidget);
    expect(find.bySemanticsLabel('Portfolio'), findsOneWidget);
    for (final key in ['tab-bar-create', 'tab-bar-feed', 'tab-bar-portfolio']) {
      final size = tester.getSize(find.byKey(ValueKey(key)));
      expect(size.height, greaterThanOrEqualTo(48), reason: key);
    }
    semantics.dispose();
  });
}
