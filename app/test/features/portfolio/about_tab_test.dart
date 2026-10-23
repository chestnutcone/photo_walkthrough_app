import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import '../../core/support/fakes.dart';
import 'support/portfolio_harness.dart';

void main() {
  Future<PortfolioHarness> pumpAbout(
    WidgetTester t, {
    String? publicEmail,
    Map<String, Object?>? me,
  }) async {
    final h = await pumpPortfolio(
      t,
      me: me,
      artist: (_) => jsonResponse(
        200,
        okEnvelope(
          profileJson(
            publicEmail: publicEmail,
            services: [
              {
                'title': 'Portraits',
                'description': 'Half-day sessions',
                'price': 'from \$900',
              },
            ],
            shows: [
              {'year': '2025', 'text': 'Salt, Porto'},
            ],
          ),
        ),
      ),
    );
    await t.tap(find.byKey(const ValueKey('tab-about')));
    await t.pumpAndSettle();
    return h;
  }

  group('AboutTab', () {
    testWidgets('shows_services_and_shows', (t) async {
      await pumpAbout(t);
      expect(find.text('AVAILABLE FOR'), findsOneWidget);
      expect(find.text('Portraits'), findsOneWidget);
      expect(find.text('from \$900'), findsOneWidget);
      expect(find.text('EXHIBITIONS & PRESS'), findsOneWidget);
      expect(find.text('Salt, Porto'), findsOneWidget);
    });

    testWidgets('copy_email_row_copies_public_email_only', (t) async {
      final h = await pumpAbout(t, publicEmail: 'studio@ines.example');
      expect(find.text('studio@ines.example'), findsOneWidget);
      await t.tap(find.byKey(const ValueKey('copy-email')));
      await t.pumpAndSettle();
      expect(h.clips, ['studio@ines.example']);
      expect(find.text('Copied'), findsOneWidget);
      expect(find.textContaining(loginEmail), findsNothing);
    });

    testWidgets('no_email_row_when_public_email_absent', (t) async {
      await pumpAbout(t, me: meJson());
      expect(find.byKey(const ValueKey('copy-email')), findsNothing);
      expect(find.text('Email'), findsNothing);
      expect(find.textContaining('@'), findsNothing);
    });

    testWidgets('write_row_opens_contact_sheet', (t) async {
      await pumpAbout(t, me: meJson());
      await t.tap(find.byKey(const ValueKey('contact-write')));
      await t.pumpAndSettle();
      expect(find.byKey(const ValueKey('contact-sheet')), findsOneWidget);
    });
  });
}
