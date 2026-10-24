import 'package:flutter/foundation.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:vela_app/features/web/web_viewer_config.dart';

import 'web_harness.dart';

const _unavailable = "This story isn't available";
const _stores = WebViewerConfig(
  appStoreUrl: 'https://apps.apple.com/app/id1',
  playStoreUrl: 'https://play.google.com/store/apps/details?id=x',
);

void main() {
  testWidgets('web_viewer_renders_story_without_session', (t) async {
    final h = await pumpWeb(t);
    expect(find.text('Salt and Rope'), findsWidgets);
    expect(find.text('Begin →'), findsOneWidget);
    expect(find.text('Sign in'), findsNothing);
    expect(find.byKey(const ValueKey('close')), findsNothing);
    expect(h.adapter.count('/v1/works/w1'), 1);
  });

  testWidgets('web_viewer_never_sends_an_authorization_header', (t) async {
    final h = await pumpWeb(t);
    await t.tap(find.text('Begin →'));
    await t.pumpAndSettle();
    await t.tap(find.text('Reply'));
    await t.pumpAndSettle();
    expect(h.adapter.requests.length, greaterThanOrEqualTo(2));
    for (final r in h.adapter.requests) {
      expect(
        r.headers.keys.map((k) => k.toLowerCase()),
        isNot(contains('authorization')),
      );
      expect(r.path, isNot(contains('/v1/me')));
      expect(r.path, isNot(contains('/v1/auth/')));
      expect(r.method, 'GET');
    }
  });

  testWidgets('web_viewer_sends_no_credentials_even_with_a_session_in_memory', (
    t,
  ) async {
    final h = await pumpWeb(t, storedSession: true);
    await t.tap(find.text('Begin →'));
    await t.pumpAndSettle();
    await t.tap(find.text('Reply'));
    await t.pumpAndSettle();
    expect(h.adapter.count('/v1/works/w1'), 1);
    expect(h.adapter.requests, isNotEmpty);
    for (final r in h.adapter.requests) {
      expect(
        r.headers.keys.map((k) => k.toLowerCase()),
        isNot(contains('authorization')),
        reason: r.path,
      );
    }
  });

  testWidgets('shared_link_path_from_the_platform_route_opens_the_story', (
    t,
  ) async {
    await pumpWeb(t, platformRoute: '/s/w1');
    expect(find.text('Salt and Rope'), findsWidgets);
    expect(find.text(_unavailable), findsNothing);
  });

  testWidgets('replies_are_visible_but_no_composer', (t) async {
    await pumpWeb(t);
    await t.tap(find.text('Begin →'));
    await t.pumpAndSettle();
    await t.tap(find.text('Reply'));
    await t.pumpAndSettle();
    expect(find.text('Lovely light'), findsOneWidget);
    expect(find.byKey(const ValueKey('composer-input')), findsNothing);
    expect(find.byKey(const ValueKey('composer-send')), findsNothing);
  });

  testWidgets('open_in_app_banner_present', (t) async {
    final h = await pumpWeb(t, config: _stores);
    expect(find.byKey(const ValueKey('open-in-app')), findsOneWidget);
    expect(find.byKey(const ValueKey('store-app-store')), findsOneWidget);
    expect(find.byKey(const ValueKey('store-google-play')), findsOneWidget);
    await t.tap(find.byKey(const ValueKey('open-in-app')));
    await t.pumpAndSettle();
    // The test platform is Android: a phone prefers the universal link.
    expect(h.launched.single.toString(), 'https://vela.example/s/w1');
    await t.tap(find.byKey(const ValueKey('store-app-store')));
    await t.pumpAndSettle();
    expect(h.launched.last.toString(), 'https://apps.apple.com/app/id1');
  });

  testWidgets('open_in_app_uses_the_custom_scheme_off_phones', (t) async {
    debugDefaultTargetPlatformOverride = TargetPlatform.linux;
    final h = await pumpWeb(t);
    await t.tap(find.byKey(const ValueKey('open-in-app')));
    await t.pumpAndSettle();
    debugDefaultTargetPlatformOverride = null;
    expect(h.launched.single.toString(), 'vela://open/s/w1');
  });

  testWidgets('open_in_app_store_buttons_hidden_when_urls_unset', (t) async {
    await pumpWeb(t);
    expect(find.byKey(const ValueKey('open-in-app')), findsOneWidget);
    expect(find.byKey(const ValueKey('store-app-store')), findsNothing);
    expect(find.byKey(const ValueKey('store-google-play')), findsNothing);
  });

  testWidgets('non_https_store_urls_are_ignored', (t) async {
    await pumpWeb(
      t,
      config: const WebViewerConfig(
        appStoreUrl: 'javascript:alert(1)',
        playStoreUrl: 'http://insecure.example/x',
      ),
    );
    expect(find.byKey(const ValueKey('store-app-store')), findsNothing);
    expect(find.byKey(const ValueKey('store-google-play')), findsNothing);
  });

  for (final status in [404, 403, 410, 401]) {
    testWidgets('unknown_or_unpublished_work_shows_not_found_$status', (
      t,
    ) async {
      await pumpWeb(t, workStatus: status);
      expect(find.text(_unavailable), findsOneWidget);
    });
  }

  testWidgets('unknown_paths_on_web_show_not_found', (t) async {
    for (final path in ['/', '/story/w1', '/feed', '/s']) {
      await pumpWeb(t, location: path);
      expect(find.text(_unavailable), findsOneWidget, reason: path);
      expect(find.text('Salt and Rope'), findsNothing, reason: path);
    }
  });
}
