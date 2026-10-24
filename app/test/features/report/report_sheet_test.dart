import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import '../../core/support/fakes.dart';
import 'report_harness.dart';

const _id = '11111111-1111-4111-8111-111111111111';

Future<void> _send(WidgetTester tester, String reason) async {
  await tester.tap(reasonTile(reason));
  await tester.pump();
  await tester.tap(sendButton);
  await tester.pumpAndSettle();
}

void main() {
  testWidgets('report_requires_sign_in_and_sends_reason_and_target', (
    tester,
  ) async {
    final rig = await pumpReport(tester, signedIn: false);
    expect(rig.signInLaunches, 1);
    expect(sendButton, findsOneWidget, reason: 'sheet opens after sign-in');

    await tester.tap(reasonTile('harassment'));
    await tester.pump();
    await tester.enterText(noteField, '  keeps messaging me  ');
    await tester.tap(sendButton);
    await tester.pumpAndSettle();

    expect(rig.bodies.single, {
      'targetType': 'reply',
      'targetId': _id,
      'reason': 'harassment',
      'note': 'keeps messaging me',
    });
  });

  testWidgets('guest_cancelling_sign_in_sends_nothing_and_opens_no_sheet', (
    tester,
  ) async {
    final rig = await pumpReport(
      tester,
      signedIn: false,
      signInSucceeds: false,
    );

    expect(rig.signInLaunches, 1);
    expect(sendButton, findsNothing);
    expect(rig.adapterCalls, isEmpty);
    expect(rig.results, [false]);
  });

  testWidgets('illegal_content_reason_is_listed_first_after_copyright', (
    tester,
  ) async {
    await pumpReport(tester);

    final order = [
      'illegal_content',
      'copyright',
      'harassment',
      'spam',
      'other',
    ];
    final tops = [for (final r in order) tester.getTopLeft(reasonTile(r)).dy];
    expect(tops, orderedEquals([...tops]..sort()));
    expect(tops.toSet(), hasLength(5));
    expect(find.text('Illegal content'), findsOneWidget);
    expect(find.text('Copyright'), findsOneWidget);
  });

  testWidgets('send_is_disabled_until_a_reason_is_chosen', (tester) async {
    final rig = await pumpReport(tester);
    await tester.tap(sendButton, warnIfMissed: false);
    await tester.pumpAndSettle();
    expect(rig.adapterCalls, isEmpty);
  });

  for (final type in ['work', 'reply', 'user']) {
    testWidgets('reports_a_${type}_target_with_its_type', (tester) async {
      final rig = await pumpReport(tester, targetType: type);
      await _send(tester, 'spam');
      expect(rig.bodies.single['targetType'], type);
      expect(rig.bodies.single.containsKey('note'), isFalse);
    });
  }

  testWidgets('success_shows_thanks_and_hides_item_locally', (tester) async {
    final rig = await pumpReport(tester);
    await _send(tester, 'illegal_content');

    expect(find.text('Thanks for letting us know'), findsOneWidget);
    expect(rig.results, isEmpty, reason: 'still open until Done');

    await tester.tap(find.byKey(const ValueKey('report-done')));
    await tester.pumpAndSettle();

    expect(rig.results, [true]);
    expect(sendButton, findsNothing);
  });

  testWidgets('swiping_the_thanks_state_away_still_reports_success', (
    tester,
  ) async {
    final rig = await pumpReport(tester);
    await _send(tester, 'spam');

    await tester.tapAt(const Offset(20, 20)); // barrier
    await tester.pumpAndSettle();

    expect(rig.results, [true]);
  });

  testWidgets('closing_without_sending_returns_false', (tester) async {
    final rig = await pumpReport(tester);
    await tester.tapAt(const Offset(20, 20));
    await tester.pumpAndSettle();
    expect(rig.results, [false]);
    expect(rig.adapterCalls, isEmpty);
  });

  testWidgets('double_tap_sends_one_report', (tester) async {
    final gate = Completer<void>();
    final rig = await pumpReport(
      tester,
      respond: () async {
        await gate.future;
        return jsonResponse(200, okEnvelope({}));
      },
    );
    await tester.tap(reasonTile('spam'));
    await tester.pump();
    await tester.tap(sendButton);
    await tester.pump();
    await tester.tap(sendButton, warnIfMissed: false);
    await tester.pump();

    gate.complete();
    await tester.pumpAndSettle();

    expect(rig.adapterCalls, hasLength(1));
  });

  testWidgets('daily_limit_error_is_explained_with_hours', (tester) async {
    await pumpReport(
      tester,
      respond: () => jsonResponse(
        429,
        errEnvelope('report_limit_reached'),
        headers: {
          'retry-after': ['18000'],
        },
      ),
    );
    await _send(tester, 'spam');

    expect(
      find.text("You've reached today's report limit. Try again in 5 hours."),
      findsOneWidget,
    );
    expect(sendButton, findsOneWidget, reason: 'sheet stays open');
  });

  testWidgets('rate_limited_error_shows_retry_after_seconds', (tester) async {
    await pumpReport(
      tester,
      respond: () => jsonResponse(
        429,
        errEnvelope('rate_limited'),
        headers: {
          'retry-after': ['30'],
        },
      ),
    );
    await _send(tester, 'spam');

    expect(
      find.text('Too many requests. Try again in 30 seconds.'),
      findsOneWidget,
    );
  });

  for (final c in {
    'target_not_found': 'This item is no longer available.',
    'cannot_report_own': "You can't report your own content.",
  }.entries) {
    testWidgets('${c.key}_has_a_clear_message', (tester) async {
      await pumpReport(
        tester,
        respond: () => jsonResponse(
          c.key == 'target_not_found' ? 404 : 400,
          errEnvelope(c.key, 'server words'),
        ),
      );
      await _send(tester, 'spam');
      expect(find.text(c.value), findsOneWidget);
      expect(find.text('server words'), findsNothing);
    });
  }

  testWidgets('network_failure_keeps_the_sheet_open_and_retry_works', (
    tester,
  ) async {
    var fail = true;
    final rig = await pumpReport(
      tester,
      respond: () {
        if (fail) throw StateError('offline');
        return jsonResponse(200, okEnvelope({}));
      },
    );
    await _send(tester, 'spam');
    expect(
      find.text("Can't reach the server. Check your connection."),
      findsOneWidget,
    );

    fail = false;
    await tester.tap(sendButton);
    await tester.pumpAndSettle();

    expect(find.text('Thanks for letting us know'), findsOneWidget);
    expect(rig.adapterCalls, hasLength(2));
  });

  testWidgets('copyright_reason_explains_and_links_to_the_dmca_page', (
    tester,
  ) async {
    final rig = await pumpReport(tester);
    expect(find.byKey(const ValueKey('report-copyright-link')), findsNothing);

    await tester.tap(reasonTile('copyright'));
    await tester.pump();
    await tester.tap(find.byKey(const ValueKey('report-copyright-link')));
    await tester.pump();

    expect(find.text('Report copyright infringement'), findsOneWidget);
    expect(rig.openedLinks.single.path, '/legal/dmca');
  });

  testWidgets('note_is_capped_at_500_characters', (tester) async {
    await pumpReport(tester);
    await tester.tap(reasonTile('other'));
    await tester.pump();
    await tester.enterText(noteField, 'x' * 600);
    await tester.pump();
    final field = tester.widget<TextField>(noteField);
    expect(field.controller!.text.length, 500);
  });

  for (final c in ['target_not_found', 'cannot_report_own']) {
    testWidgets('${c}_offers_no_retry_only_close', (tester) async {
      final rig = await pumpReport(
        tester,
        respond: () =>
            jsonResponse(c == 'target_not_found' ? 404 : 400, errEnvelope(c)),
      );
      await _send(tester, 'spam');
      expect(sendButton, findsNothing);
      expect(find.text('Try again'), findsNothing);
      await tester.tap(find.byKey(const ValueKey('report-close')));
      await tester.pumpAndSettle();
      expect(rig.results, [false]);
    });
  }

  testWidgets('user_reports_do_not_claim_the_artist_is_hidden', (tester) async {
    await pumpReport(tester, targetType: 'user');
    await _send(tester, 'spam');
    expect(find.textContaining('hidden'), findsNothing);
    expect(find.textContaining("We'll review"), findsOneWidget);
  });

  testWidgets('work_reports_say_the_item_is_hidden', (tester) async {
    await pumpReport(tester, targetType: 'work');
    await _send(tester, 'spam');
    expect(find.textContaining('hidden'), findsOneWidget);
  });
}
