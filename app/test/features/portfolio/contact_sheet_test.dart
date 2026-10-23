import 'dart:async';
import 'dart:convert';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:vela_app/features/story/inquiry_sheet_hook.dart';

import '../../core/support/fakes.dart';
import 'support/portfolio_harness.dart';

Finder key(String k) => find.byKey(ValueKey(k));

Future<PortfolioHarness> openContact(
  WidgetTester t, {
  Responder? inquiry,
  bool signedIn = true,
  bool signInSucceeds = false,
}) async {
  final h = await pumpPortfolio(
    t,
    me: signedIn ? meJson() : null,
    inquiry: inquiry,
    signInSucceeds: signInSucceeds,
  );
  await t.tap(key('portfolio-contact'));
  await t.pumpAndSettle();
  return h;
}

Future<void> fill(
  WidgetTester t, {
  String email = 'me@x.io',
  String msg = 'Hi',
}) async {
  await t.enterText(key('contact-email'), email);
  await t.enterText(key('contact-message'), msg);
  await t.pump();
}

bool sendEnabled(WidgetTester t) =>
    t.widget<FilledButton>(key('contact-send')).onPressed != null;

void main() {
  group('ContactSheet', () {
    testWidgets('contact_send_disabled_until_valid', (t) async {
      await openContact(t);
      expect(sendEnabled(t), isFalse);
      await fill(t, email: 'nope', msg: 'Hello');
      expect(sendEnabled(t), isFalse);
      await fill(t, email: 'a@b.co', msg: '   ');
      expect(sendEnabled(t), isFalse);
      await fill(t, email: ' a@b.co ', msg: 'Hello');
      expect(sendEnabled(t), isTrue);
      await fill(t, email: '@', msg: 'Hello');
      expect(sendEnabled(t), isFalse);
    });

    testWidgets('lists_the_five_types_and_defaults_to_commission', (t) async {
      await openContact(t);
      for (final type in const [
        'Commission',
        'Buy a print',
        'Licensing',
        'Collaboration',
        'Just saying hi',
      ]) {
        expect(key('contact-chip-$type'), findsOneWidget);
      }
    });

    testWidgets('selected_type_and_email_are_sent_trimmed', (t) async {
      final h = await openContact(t);
      await t.tap(key('contact-chip-Licensing'));
      await t.pump();
      await fill(t, email: ' me@x.io ', msg: '  Hello\u0007 there \n');
      await t.tap(key('contact-send'));
      await t.pumpAndSettle();
      final body =
          jsonDecode(h.requestsTo('/inquiries').single.data as String) as Map;
      expect(
        h.requestsTo('/inquiries').single.path,
        '/v1/artists/a1/inquiries',
      );
      expect(body['type'], 'Licensing');
      expect(body['email'], 'me@x.io');
      expect(body['message'], 'Hello there');
      expect(body.containsKey('workId'), isFalse);
      expect(jsonEncode(body), isNot(contains(loginEmail)));
    });

    testWidgets('contact_success_state_shows_first_name', (t) async {
      await openContact(t);
      await fill(t, email: 'me@x.io', msg: 'Hello');
      await t.tap(key('contact-send'));
      await t.pumpAndSettle();
      expect(find.text('Sent to Ines.'), findsOneWidget);
      expect(
        find.textContaining("Your commission inquiry is in Ines's inbox."),
        findsOneWidget,
      );
      expect(
        find.textContaining('Usually replies within 2 days.'),
        findsOneWidget,
      );
      expect(find.textContaining('Replies go to me@x.io.'), findsOneWidget);
      await t.tap(find.text('Back to portfolio'));
      await t.pumpAndSettle();
      expect(key('contact-sheet'), findsNothing);
    });

    testWidgets('failure_keeps_text_with_inline_error', (t) async {
      await openContact(
        t,
        inquiry: (_) => jsonResponse(500, errEnvelope('server_error')),
      );
      await fill(t, email: 'me@x.io', msg: 'Hello there');
      await t.tap(key('contact-send'));
      await t.pumpAndSettle();
      expect(key('contact-error'), findsOneWidget);
      expect(find.text('Hello there'), findsOneWidget);
      expect(find.text('me@x.io'), findsOneWidget);
      expect(sendEnabled(t), isTrue);
    });

    testWidgets('offline_failure_says_so', (t) async {
      await openContact(t, inquiry: (_) => throw StateError('x'));
      await fill(t);
      await t.tap(key('contact-send'));
      await t.pumpAndSettle();
      expect(find.textContaining("offline"), findsOneWidget);
    });

    testWidgets('rate_limited_shows_retry_after_wait', (t) async {
      await openContact(
        t,
        inquiry: (_) => jsonResponse(
          429,
          errEnvelope('rate_limited'),
          headers: {
            'retry-after': ['120'],
          },
        ),
      );
      await fill(t);
      await t.tap(key('contact-send'));
      await t.pumpAndSettle();
      expect(find.textContaining('2 minutes'), findsOneWidget);
    });

    testWidgets('double_tap_sends_once', (t) async {
      final done = Completer<ResponseBody>();
      final h = await openContact(t, inquiry: (_) => done.future);
      await fill(t);
      await t.tap(key('contact-send'));
      await t.pump();
      expect(sendEnabled(t), isFalse);
      await t.tap(key('contact-send'), warnIfMissed: false);
      await t.pump(const Duration(milliseconds: 100));
      await t.pump(const Duration(milliseconds: 100));
      expect(h.requestsTo('/inquiries'), hasLength(1));
      done.complete(
        jsonResponse(
          200,
          okEnvelope({
            'type': 'Commission',
            'id': 'i1',
            'artistId': 'a1',
            'message': 'm',
            'createdAt': '2026-10-01T10:00:00Z',
          }),
        ),
      );
      await t.pumpAndSettle();
      expect(find.text('Sent to Ines.'), findsOneWidget);
    });

    testWidgets('message_is_limited_to_the_contract_length', (t) async {
      await openContact(t);
      await t.enterText(key('contact-message'), 'x' * 2100);
      await t.pump();
      final field = t.widget<TextField>(key('contact-message'));
      expect(field.controller!.text.length, 2000);
    });

    testWidgets('guest_must_sign_in_before_the_sheet_opens', (t) async {
      final h = await openContact(t, signedIn: false);
      expect(h.signInLaunches, hasLength(1));
      expect(key('contact-sheet'), findsNothing);
    });

    testWidgets('guest_who_signs_in_gets_the_sheet', (t) async {
      final h = await openContact(t, signedIn: false, signInSucceeds: true);
      expect(h.signInLaunches, hasLength(1));
      expect(key('contact-sheet'), findsOneWidget);
    });

    testWidgets('contact_prefills_work_and_type_for_print_inquiry', (t) async {
      final h = await pumpPortfolio(
        t,
        me: meJson(),
        works: (_) => jsonResponse(200, listPage([workJson('w1')])),
      );
      final ctx = t.element(find.byKey(const ValueKey('portfolio-contact')));
      final container = ProviderScope.containerOf(ctx);
      unawaited(
        container.read(openInquirySheetProvider)(
          ctx,
          artistId: 'a1',
          workId: 'w1',
          type: 'Buy a print',
        ),
      );
      await t.pumpAndSettle();
      expect(key('contact-sheet'), findsOneWidget);
      expect(find.text('About the work'), findsOneWidget);
      expect(find.text('Title w1'), findsWidgets);
      await fill(t);
      await t.tap(key('contact-send'));
      await t.pumpAndSettle();
      final body =
          jsonDecode(h.requestsTo('/inquiries').single.data as String) as Map;
      expect(body['type'], 'Buy a print');
      expect(body['workId'], 'w1');
    });

    testWidgets('sheet_cannot_be_dismissed_while_sending', (t) async {
      final done = Completer<ResponseBody>();
      await openContact(t, inquiry: (_) => done.future);
      await fill(t);
      await t.tap(key('contact-send'));
      await t.pump();
      await t.tapAt(const Offset(10, 10));
      await t.pump(const Duration(milliseconds: 400));
      expect(key('contact-sheet'), findsOneWidget);
      done.complete(jsonResponse(500, errEnvelope('server_error')));
      await t.pumpAndSettle();
      await t.tapAt(const Offset(10, 10));
      await t.pumpAndSettle();
      expect(key('contact-sheet'), findsNothing);
    });

    testWidgets('fields_have_accessible_labels', (t) async {
      final handle = t.ensureSemantics();
      await openContact(t);
      expect(find.bySemanticsLabel(RegExp('Your email')), findsWidgets);
      expect(find.bySemanticsLabel(RegExp('Message')), findsWidgets);
      handle.dispose();
    });

    testWidgets('work_lookup_failure_falls_back_to_the_artist', (t) async {
      final h = await pumpPortfolio(
        t,
        me: meJson(),
        work: (_) => jsonResponse(500, errEnvelope('server_error')),
      );
      final ctx = t.element(find.byKey(const ValueKey('portfolio-contact')));
      final container = ProviderScope.containerOf(ctx);
      unawaited(
        container.read(openInquirySheetProvider)(
          ctx,
          artistId: 'a1',
          workId: 'w1',
          type: 'Buy a print',
        ),
      );
      await t.pumpAndSettle();
      expect(key('contact-sheet'), findsOneWidget);
      expect(find.text('About the work'), findsNothing);
      await fill(t);
      await t.tap(key('contact-send'));
      await t.pumpAndSettle();
      expect(find.text('Sent to the artist.'), findsOneWidget);
      expect(h.requestsTo('/inquiries'), hasLength(1));
    });
  });
}
