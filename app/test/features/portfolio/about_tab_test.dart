import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:vela_app/features/onboarding/legal_links.dart';

import '../../core/support/fakes.dart';
import 'support/portfolio_harness.dart';

void main() {
  Future<PortfolioHarness> pumpAbout(
    WidgetTester t, {
    String? publicEmail,
    String? website,
    Map<String, Object?>? me,
    List<Uri>? launched,
    bool launchSucceeds = true,
  }) async {
    final h = await pumpPortfolio(
      t,
      me: me,
      overrides: [
        legalUrlLauncherProvider.overrideWithValue((uri) async {
          launched?.add(uri);
          return launchSucceeds;
        }),
      ],
      artist: (_) => jsonResponse(
        200,
        okEnvelope(
          profileJson(
            publicEmail: publicEmail,
            website: website,
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

    testWidgets('https_website_row_opens_the_browser_not_the_clipboard', (
      t,
    ) async {
      final launched = <Uri>[];
      final h = await pumpAbout(
        t,
        website: ' https://ines.example/work ',
        launched: launched,
      );
      expect(find.text('https://ines.example/work'), findsOneWidget);
      expect(find.text('Open'), findsOneWidget);
      await t.tap(find.byKey(const ValueKey('open-website')));
      await t.pumpAndSettle();
      expect(launched, [Uri.parse('https://ines.example/work')]);
      expect(h.clips, isEmpty);
      expect(find.byKey(const ValueKey('copy-website')), findsNothing);
    });

    testWidgets('a_failed_open_shows_a_message', (t) async {
      await pumpAbout(
        t,
        website: 'https://ines.example',
        launched: [],
        launchSucceeds: false,
      );
      await t.tap(find.byKey(const ValueKey('open-website')));
      await t.pumpAndSettle();
      expect(find.text("Couldn't open the page. Try again later."), findsOne);
    });

    for (final site in [
      'http://ines.example',
      'javascript:alert(1)',
      'ines.example',
    ]) {
      testWidgets('non_https_website_is_never_launched_and_stays_copyable: '
          '$site', (t) async {
        final launched = <Uri>[];
        final h = await pumpAbout(t, website: site, launched: launched);
        expect(find.byKey(const ValueKey('open-website')), findsNothing);
        await t.tap(find.byKey(const ValueKey('copy-website')));
        await t.pumpAndSettle();
        expect(launched, isEmpty);
        expect(h.clips, [site]);
      });
    }

    test('website_rule_is_https_with_a_host_only', () {
      expect(launchableWebsiteUri('https://a.example/x'), isNotNull);
      expect(launchableWebsiteUri('HTTPS://a.example'), isNotNull);
      expect(launchableWebsiteUri('http://a.example'), isNull);
      expect(launchableWebsiteUri('https://'), isNull);
      expect(launchableWebsiteUri('https://user:pw@a.example'), isNull);
      expect(launchableWebsiteUri('ftp://a.example'), isNull);
      expect(launchableWebsiteUri('not a url'), isNull);
    });
  });
}
