import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:vela_app/features/replies/bidi.dart';
import 'package:flutter_test/flutter_test.dart';

import 'dart:async';

import 'support/fake_replies_api.dart';
import 'support/sheet_harness.dart';

Future<void> _type(WidgetTester t, String text) async {
  await t.enterText(composerInput, text);
  await t.pump();
}

bool _sendEnabled(WidgetTester t) =>
    t.widget<ButtonStyleButton>(composerSend).onPressed != null;

void main() {
  testWidgets('lists_replies_and_empty_state', (t) async {
    await pumpSheet(
      t,
      replies: [
        reply('r1', name: 'Teo'),
        reply('r2'),
      ],
    );
    expect(find.text('REPLIES ON'), findsOneWidget);
    expect(find.text('The rope'), findsOneWidget);
    expect(find.text('text r1'), findsOneWidget);
    expect(find.text('text r2'), findsOneWidget);
    expect(find.text('Teo'), findsNWidgets(2));
    expect(find.text('No replies on this detail yet.'), findsNothing);
  });

  testWidgets('empty_state_when_no_replies', (t) async {
    await pumpSheet(t, replies: []);
    expect(find.text('No replies on this detail yet.'), findsOneWidget);
  });

  testWidgets('load_failure_shows_retry', (t) async {
    final api = FakeRepliesApi();
    await pumpSheet(t, fake: api, open: false);
    await t.tap(find.text('open'));
    await t.pump();
    await t.pump();
    api.listCalls[0].done.completeError(apiError('server_error', status: 500));
    await t.pumpAndSettle();
    expect(find.text('Could not load replies.'), findsOneWidget);
    await t.tap(find.text('Try again'));
    await t.pump();
    await t.pump();
    expect(api.listCalls, hasLength(2));
    api.listCalls[1].reply([reply('r1')]);
    await t.pumpAndSettle();
    expect(find.text('text r1'), findsOneWidget);
  });

  testWidgets('send_disabled_for_blank_text', (t) async {
    await pumpSheet(t, replies: []);
    expect(_sendEnabled(t), isFalse);
    await _type(t, '   \n  ');
    expect(_sendEnabled(t), isFalse);
    await _type(t, ' hi ');
    expect(_sendEnabled(t), isTrue);
  });

  testWidgets('send_prompts_sign_in_for_guest_and_keeps_text_if_cancelled', (
    t,
  ) async {
    final h = await pumpSheet(t, replies: [], signedIn: false);
    await _type(t, 'hello');
    await t.tap(composerSend);
    await t.pumpAndSettle();
    expect(h.signInLaunches, 1);
    expect(h.api.addCalls, isEmpty);
    expect(find.text('hello'), findsOneWidget); // still in the input
  });

  testWidgets('guest_who_signs_in_then_sends', (t) async {
    final h = await pumpSheet(
      t,
      replies: [],
      signedIn: false,
      signInSucceeds: true,
    );
    await _type(t, 'hello');
    await t.tap(composerSend);
    await t.pumpAndSettle();
    expect(h.signInLaunches, 1);
    expect(h.api.addCalls.single.body, 'hello');
  });

  testWidgets('send_appends_reply_and_clears_input', (t) async {
    final h = await pumpSheet(t, replies: [reply('r1')]);
    await _type(t, '  lovely light  ');
    await t.tap(composerSend);
    await t.pump();
    expect(h.api.addCalls.single.body, 'lovely light'); // trimmed
    h.api.addCalls.single.succeed(
      reply('r2', authorId: 'me', name: 'Me', body: 'from server'),
    );
    await t.pumpAndSettle();
    expect(find.text('from server'), findsOneWidget);
    expect(find.text('lovely light'), findsNothing);
    expect(t.widget<TextField>(composerInput).controller!.text, isEmpty);
  });

  testWidgets('double_tap_on_send_posts_once', (t) async {
    final h = await pumpSheet(t, replies: []);
    await _type(t, 'once');
    await t.tap(composerSend);
    await t.pump();
    await t.tap(composerSend, warnIfMissed: false);
    await t.pump();
    expect(h.api.addCalls, hasLength(1));
    h.api.addCalls.single.succeed(reply('r2', body: 'once'));
    await t.pumpAndSettle();
  });

  testWidgets('failed_send_keeps_text_and_shows_error', (t) async {
    final h = await pumpSheet(t, replies: []);
    await _type(t, 'keep me');
    await t.tap(composerSend);
    await t.pump();
    h.api.addCalls.single.fail(apiError('server_error', status: 500));
    await t.pumpAndSettle();
    expect(
      find.text('Something went wrong. Please try again.'),
      findsOneWidget,
    );
    expect(t.widget<TextField>(composerInput).controller!.text, 'keep me');
    expect(_sendEnabled(t), isTrue); // can retry
  });

  testWidgets('rate_limited_send_shows_retry_message', (t) async {
    final h = await pumpSheet(t, replies: []);
    await _type(t, 'x');
    await t.tap(composerSend);
    await t.pump();
    h.api.addCalls.single.fail(
      apiError('rate_limited', status: 429, retryAfter: '12'),
    );
    await t.pumpAndSettle();
    expect(
      find.text('You are replying too fast. Try again in 12 seconds.'),
      findsOneWidget,
    );
  });

  testWidgets('text_over_1000_chars_is_blocked_in_input', (t) async {
    await pumpSheet(t, replies: []);
    await _type(t, 'a' * 1200);
    expect(t.widget<TextField>(composerInput).controller!.text.length, 1000);
    expect(find.text('1000 / 1000'), findsOneWidget);
    // Typing one more is refused.
    await _type(t, '${'a' * 1000}b');
    expect(t.widget<TextField>(composerInput).controller!.text, 'a' * 1000);
  });

  testWidgets('limit_counts_code_points_not_utf16_units', (t) async {
    await pumpSheet(t, replies: []);
    await _type(t, '😀' * 1001); // 2 UTF-16 units each
    final text = t.widget<TextField>(composerInput).controller!.text;
    expect(text.runes.length, 1000);
    expect(text.length, 2000); // no half surrogate pair left behind
  });

  testWidgets('tab_and_control_chars_are_stripped_newline_kept', (t) async {
    await pumpSheet(t, replies: []);
    await _type(t, 'a\tb\u0007c\nd');
    expect(t.widget<TextField>(composerInput).controller!.text, 'abc\nd');
  });

  testWidgets('counter_only_near_the_limit', (t) async {
    await pumpSheet(t, replies: []);
    await _type(t, 'a' * 10);
    expect(find.textContaining(' / 1000'), findsNothing);
    await _type(t, 'a' * 950);
    expect(find.text('950 / 1000'), findsOneWidget);
  });

  testWidgets('own_reply_can_be_deleted_others_can_be_reported', (t) async {
    final h = await pumpSheet(
      t,
      replies: [
        reply('mine', authorId: 'me', name: 'Me'),
        reply('theirs'),
      ],
      reportResult: true,
    );
    expect(find.byKey(const ValueKey('delete-mine')), findsOneWidget);
    expect(find.byKey(const ValueKey('report-mine')), findsNothing);
    expect(find.byKey(const ValueKey('report-theirs')), findsOneWidget);
    expect(find.byKey(const ValueKey('delete-theirs')), findsNothing);

    await t.tap(find.byKey(const ValueKey('report-theirs')));
    await t.pumpAndSettle();
    expect(h.reports, ['reply/theirs']);
    expect(find.text('text theirs'), findsNothing); // hidden locally

    await t.tap(find.byKey(const ValueKey('delete-mine')));
    await t.pumpAndSettle();
    expect(find.text('Delete this reply?'), findsOneWidget);
    await t.tap(find.text('Cancel'));
    await t.pumpAndSettle();
    expect(h.api.deleteCalls, isEmpty);
    expect(find.text('text mine'), findsOneWidget);

    await t.tap(find.byKey(const ValueKey('delete-mine')));
    await t.pumpAndSettle();
    await t.tap(find.widgetWithText(TextButton, 'Delete'));
    await t.pump();
    expect(find.text('text mine'), findsNothing); // optimistic
    h.api.deleteCalls.single.fail(apiError('server_error', status: 500));
    await t.pumpAndSettle();
    expect(find.text('text mine'), findsOneWidget); // rolled back
    expect(find.text('Could not delete the reply.'), findsOneWidget);
  });

  testWidgets('report_declined_keeps_reply_visible', (t) async {
    await pumpSheet(t, replies: [reply('theirs')], reportResult: false);
    await t.tap(find.byKey(const ValueKey('report-theirs')));
    await t.pumpAndSettle();
    expect(find.text('text theirs'), findsOneWidget);
  });

  testWidgets('hidden_for_others_reply_is_labelled_for_its_author', (t) async {
    await pumpSheet(t, replies: [reply('mine', authorId: 'me', hidden: true)]);
    expect(find.text('Only you can see this'), findsOneWidget);
  });

  testWidgets('rtl_and_emoji_render', (t) async {
    const arabic = 'مرحبا بالعالم';
    const hebrew = 'שלום עולם';
    const family = 'Lovely 👨‍👩‍👧‍👦 shot 🇺🇸';
    await pumpSheet(
      t,
      replies: [
        reply('a', body: arabic),
        reply('h', body: hebrew),
        reply('e', body: family),
      ],
    );
    TextDirection dirOf(String s) =>
        t.widget<Text>(find.text(s)).textDirection!;
    expect(dirOf(arabic), TextDirection.rtl);
    expect(dirOf(hebrew), TextDirection.rtl);
    expect(dirOf(family), TextDirection.ltr);
    expect(t.takeException(), isNull);
    // Composer follows what is typed.
    await _type(t, hebrew);
    expect(t.widget<TextField>(composerInput).textDirection, TextDirection.rtl);
    await _type(t, family);
    expect(t.widget<TextField>(composerInput).textDirection, TextDirection.ltr);
  });

  testWidgets('read_only_has_no_composer_or_actions', (t) async {
    await pumpSheet(
      t,
      replies: [
        reply('mine', authorId: 'me', name: 'Me'),
        reply('theirs'),
      ],
      readOnly: true,
    );
    expect(composerInput, findsNothing);
    expect(composerSend, findsNothing);
    expect(find.byKey(const ValueKey('delete-mine')), findsNothing);
    expect(find.byKey(const ValueKey('report-theirs')), findsNothing);
    expect(find.text('text theirs'), findsOneWidget);
  });

  testWidgets('future_completes_when_sheet_is_dismissed', (t) async {
    final h = await pumpSheet(t, replies: []);
    expect(h.dismissed, 0);
    await t.tap(find.text('Done'));
    await t.pumpAndSettle();
    expect(h.dismissed, 1);
    expect(find.text('REPLIES ON'), findsNothing);
  });

  testWidgets('dismiss_by_scrim_tap_also_completes', (t) async {
    final h = await pumpSheet(t, replies: []);
    await t.tapAt(const Offset(195, 20));
    await t.pumpAndSettle();
    expect(h.dismissed, 1);
  });

  testWidgets('closing_with_a_request_in_flight_does_not_throw', (t) async {
    final h = await pumpSheet(t, replies: []);
    await _type(t, 'x');
    await t.tap(composerSend);
    await t.pump();
    await t.tap(find.text('Done'));
    await t.pumpAndSettle();
    h.api.addCalls.single.succeed(reply('r2', body: 'x'));
    await t.pumpAndSettle();
    expect(t.takeException(), isNull);
  });

  testWidgets('composer_stays_visible_with_keyboard_and_list_scrolls', (
    t,
  ) async {
    await pumpSheet(t, replies: [for (var i = 0; i < 12; i++) reply('r$i')]);
    t.view.viewInsets = const FakeViewPadding(bottom: 300);
    addTearDown(t.view.resetViewInsets);
    await t.pumpAndSettle();
    final rect = t.getRect(composerInput);
    expect(rect.bottom, lessThanOrEqualTo(844 - 300));
    expect(rect.top, greaterThan(0));
    await t.drag(find.byType(ListView), const Offset(0, -200));
    await t.pumpAndSettle();
    expect(t.takeException(), isNull);
  });

  testWidgets('scrolling_to_the_end_loads_the_next_page', (t) async {
    final api = FakeRepliesApi();
    await pumpSheet(t, fake: api, open: false);
    await t.tap(find.text('open'));
    await t.pump();
    await t.pump();
    api.listCalls[0].reply([
      for (var i = 0; i < 12; i++) reply('r$i'),
    ], next: 'c1');
    await t.pumpAndSettle();
    await t.drag(find.byType(ListView), const Offset(0, -3000));
    await t.pump();
    await t.pump(const Duration(milliseconds: 100));
    expect(api.listCalls, hasLength(2));
    expect(api.listCalls[1].cursor, 'c1');
    api.listCalls[1].reply([reply('r11'), reply('r12')]);
    await t.pump();
    await t.pump(const Duration(milliseconds: 100));
    await t.drag(find.byType(ListView), const Offset(0, -3000));
    await t.pumpAndSettle();
    expect(find.text('text r12'), findsOneWidget);
  });

  testWidgets('semantics_labels_on_buttons', (t) async {
    final h = t.ensureSemantics();
    await pumpSheet(t, replies: [reply('theirs')]);
    expect(find.bySemanticsLabel('Send reply'), findsOneWidget);
    expect(find.bySemanticsLabel('Report reply'), findsOneWidget);
    expect(find.bySemanticsLabel('Close replies'), findsOneWidget);
    h.dispose();
  });

  testWidgets('enter_submits_via_keyboard_and_send_action', (t) async {
    final h = await pumpSheet(t, replies: []);
    await _type(t, 'via enter');
    await t.tap(composerInput);
    await t.sendKeyEvent(LogicalKeyboardKey.enter);
    await t.pump();
    expect(h.api.addCalls.single.body, 'via enter');
    h.api.addCalls.single.succeed(reply('r2', body: 'via enter'));
    await t.pumpAndSettle();
    await _type(t, 'via action');
    await t.testTextInput.receiveAction(TextInputAction.send);
    await t.pump();
    expect(h.api.addCalls, hasLength(2));
    h.api.addCalls[1].succeed(reply('r3', body: 'via action'));
    await t.pumpAndSettle();
  });

  testWidgets('shift_enter_does_not_submit', (t) async {
    final h = await pumpSheet(t, replies: []);
    await _type(t, 'line');
    await t.tap(composerInput);
    await t.sendKeyDownEvent(LogicalKeyboardKey.shiftLeft);
    await t.sendKeyEvent(LogicalKeyboardKey.enter);
    await t.sendKeyUpEvent(LogicalKeyboardKey.shiftLeft);
    await t.pump();
    expect(h.api.addCalls, isEmpty);
  });

  testWidgets('actions_have_44pt_hit_area', (t) async {
    await pumpSheet(
      t,
      replies: [
        reply('mine', authorId: 'me'),
        reply('theirs'),
      ],
    );
    for (final k in ['delete-mine', 'report-theirs']) {
      final size = t.getSize(find.byKey(ValueKey(k)));
      expect(size.width, greaterThanOrEqualTo(44), reason: k);
      expect(size.height, greaterThanOrEqualTo(44), reason: k);
    }
  });

  testWidgets('no_actions_while_viewer_is_loading', (t) async {
    final api = FakeRepliesApi()..meGate = Completer<void>();
    await pumpSheet(
      t,
      replies: [
        reply('mine', authorId: 'me'),
        reply('theirs'),
      ],
      fake: api,
    );
    expect(find.text('text theirs'), findsOneWidget);
    expect(find.byKey(const ValueKey('report-theirs')), findsNothing);
    expect(find.byKey(const ValueKey('delete-mine')), findsNothing);
    api.meGate!.complete();
    await t.pumpAndSettle();
    expect(find.byKey(const ValueKey('report-theirs')), findsOneWidget);
    expect(find.byKey(const ValueKey('delete-mine')), findsOneWidget);
  });

  testWidgets('me_failure_gives_no_delete_and_sent_reply_is_own', (t) async {
    final api = FakeRepliesApi()..meFails = true;
    final h = await pumpSheet(
      t,
      replies: [
        reply('mine', authorId: 'me'),
        reply('theirs'),
      ],
      fake: api,
    );
    expect(find.byKey(const ValueKey('delete-mine')), findsNothing);
    expect(find.byKey(const ValueKey('report-theirs')), findsOneWidget);
    await _type(t, 'new one');
    await t.tap(composerSend);
    await t.pump();
    h.api.addCalls.single.succeed(
      reply('sent', authorId: 'zz', body: 'new one'),
    );
    await t.pumpAndSettle();
    expect(find.byKey(const ValueKey('delete-sent')), findsOneWidget);
    expect(find.byKey(const ValueKey('report-sent')), findsNothing);
  });

  testWidgets('short_first_page_auto_loads_the_next', (t) async {
    final api = FakeRepliesApi();
    await pumpSheet(t, fake: api, open: false);
    await t.tap(find.text('open'));
    await t.pump();
    await t.pump();
    api.listCalls[0].reply([reply('r1')], next: 'c1');
    await t.pump();
    await t.pump(const Duration(milliseconds: 100));
    expect(api.listCalls, hasLength(2));
    expect(api.listCalls[1].cursor, 'c1');
    api.listCalls[1].reply([reply('r2')]);
    await t.pumpAndSettle();
    expect(find.text('text r2'), findsOneWidget);
  });

  testWidgets('sending_state_is_announced', (t) async {
    final sem = t.ensureSemantics();
    final h = await pumpSheet(t, replies: []);
    await _type(t, 'x');
    await t.tap(composerSend);
    await t.pump();
    expect(find.bySemanticsLabel('Sending reply'), findsOneWidget);
    h.api.addCalls.single.succeed(reply('r2', body: 'x'));
    await t.pumpAndSettle();
    expect(find.bySemanticsLabel('Send reply'), findsOneWidget);
    sem.dispose();
  });

  test('digits_and_punctuation_are_not_strong_rtl', () {
    expect(firstStrongDirection('١٢ apples'), TextDirection.ltr);
    expect(firstStrongDirection('«١٢» مرحبا'), TextDirection.rtl);
    expect(firstStrongDirection('١٢٣'), isNull);
  });

  testWidgets('grabber_is_36_by_4_and_input_is_a_pill', (t) async {
    await pumpSheet(t, replies: []);
    expect(
      t.getSize(find.byKey(const ValueKey('sheet-grabber'))),
      const Size(36, 4),
    );
    final deco = t.widget<TextField>(composerInput).decoration!;
    final border = deco.border! as OutlineInputBorder;
    expect(border.borderRadius.topLeft.x, 99);
  });
}
