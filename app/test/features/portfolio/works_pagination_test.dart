import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:vela_app/core/api/api_providers.dart';
import 'package:vela_app/features/portfolio/portfolio_screen.dart';

import '../../core/support/fakes.dart';
import 'support/portfolio_harness.dart';

void main() {
  testWidgets('scrolling_to_the_end_loads_the_next_page_by_cursor', (t) async {
    final h = await pumpPortfolio(
      t,
      works: (o) {
        final cursor = o.queryParameters['cursor'];
        return jsonResponse(
          200,
          cursor == null
              ? listPage([
                  for (var i = 0; i < 8; i++) workJson('w$i'),
                ], next: 'c2')
              : listPage([workJson('w8'), workJson('w0')]),
        );
      },
    );
    // The view is tall, so the prefetch fires without a scroll.
    final cursors = h
        .requestsTo('/works')
        .map((r) => r.queryParameters['cursor'])
        .toList();
    expect(cursors, [null, 'c2']);
    expect(find.byKey(const ValueKey('work-tile-w8')), findsOneWidget);
    // The duplicate id from the second page is shown once.
    expect(find.byKey(const ValueKey('work-tile-w0')), findsNothing);
    expect(find.byKey(const ValueKey('featured-story')), findsOneWidget);
  });

  testWidgets('response_from_before_a_session_change_is_dropped', (t) async {
    final first = Completer<ResponseBody>();
    var calls = 0;
    await pumpPortfolio(
      t,
      works: (o) {
        calls++;
        return calls == 1
            ? first.future
            : jsonResponse(200, listPage([workJson('fresh')]));
      },
    );
    final container = ProviderScope.containerOf(
      t.element(find.byType(PortfolioScreen)),
    );
    container.read(sessionEpochProvider.notifier).bump();
    await t.pumpAndSettle();
    expect(find.text('Title fresh'), findsWidgets);
    // The fake ignores the cancel token, so the old request still completes.
    first.complete(jsonResponse(200, listPage([workJson('stale')])));
    await t.pumpAndSettle();
    expect(find.text('Title stale'), findsNothing);
    expect(find.text('Title fresh'), findsWidgets);
  });

  testWidgets('load_more_failure_keeps_items_and_offers_retry', (t) async {
    var fail = true;
    await pumpPortfolio(
      t,
      works: (o) {
        if (o.queryParameters['cursor'] == null) {
          return jsonResponse(
            200,
            listPage([for (var i = 0; i < 4; i++) workJson('w$i')], next: 'c2'),
          );
        }
        return fail
            ? jsonResponse(500, errEnvelope('server_error'))
            : jsonResponse(200, listPage([workJson('w9')]));
      },
    );
    expect(find.byKey(const ValueKey('work-tile-w1')), findsOneWidget);
    expect(find.byKey(const ValueKey('works-more-retry')), findsOneWidget);
    fail = false;
    await t.tap(find.byKey(const ValueKey('works-more-retry')));
    await t.pumpAndSettle();
    expect(find.byKey(const ValueKey('work-tile-w9')), findsOneWidget);
  });
}
