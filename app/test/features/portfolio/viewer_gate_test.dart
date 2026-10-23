import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import '../../core/support/fakes.dart';
import 'support/portfolio_harness.dart';

Finder key(String k) => find.byKey(ValueKey(k));

void main() {
  testWidgets('actions_are_held_until_me_resolves', (t) async {
    final pending = Completer<ResponseBody>();
    await pumpPortfolio(t, me: meJson(), meResponse: (_) => pending.future);
    // pumpPortfolio settles; `me` is still in flight.
    expect(key('portfolio-follow'), findsNothing);
    expect(key('portfolio-contact'), findsNothing);
    expect(key('portfolio-menu'), findsNothing);
    expect(key('portfolio-actions-loading'), findsOneWidget);
    pending.complete(jsonResponse(200, okEnvelope(meJson())));
    await t.pumpAndSettle();
    expect(key('portfolio-follow'), findsOneWidget);
    expect(key('portfolio-menu'), findsOneWidget);
    expect(key('portfolio-actions-loading'), findsNothing);
  });

  testWidgets('me_failure_shows_retry_instead_of_actions', (t) async {
    var fail = true;
    await pumpPortfolio(
      t,
      me: meJson(),
      meResponse: (_) => fail
          ? jsonResponse(500, errEnvelope('server_error'))
          : jsonResponse(200, okEnvelope(meJson())),
    );
    expect(key('portfolio-follow'), findsNothing);
    expect(key('portfolio-contact'), findsNothing);
    expect(key('portfolio-menu'), findsNothing);
    expect(key('portfolio-me-retry'), findsOneWidget);
    fail = false;
    await t.tap(key('portfolio-me-retry'));
    await t.pumpAndSettle();
    expect(key('portfolio-follow'), findsOneWidget);
  });

  testWidgets('guest_sees_actions_without_waiting_for_me', (t) async {
    await pumpPortfolio(t);
    expect(key('portfolio-follow'), findsOneWidget);
    expect(key('portfolio-contact'), findsOneWidget);
  });
}
