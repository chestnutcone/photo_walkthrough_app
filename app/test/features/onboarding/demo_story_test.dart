import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';
import 'package:vela_app/core/api/api_providers.dart';
import 'package:vela_app/core/theme/app_theme.dart';
import 'package:vela_app/features/onboarding/demo_story.dart';

import '../../core/support/fakes.dart';
import 'support/onboarding_harness.dart';

void main() {
  // Decoded assets are cached globally and a decode started in one test's
  // fake-async zone never finishes in the next.
  setUp(() {
    imageCache.clear();
    imageCache.clearLiveImages();
    rootBundle.evict('assets/demo/demo_story.json');
    rootBundle.evict('assets/demo/demo_photo.png');
  });

  testWidgets('demo_story_plays_without_network', (tester) async {
    tester.view.physicalSize = phone;
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.reset);
    final adapter = FakeAdapter((o) {
      fail('network used: ${o.path}');
    });
    final router = GoRouter(
      initialLocation: '/demo',
      routes: [
        GoRoute(path: '/', builder: (_, _) => const Text('route:home')),
        GoRoute(path: '/demo', builder: (_, _) => const DemoStoryScreen()),
      ],
    );
    addTearDown(router.dispose);
    await tester.pumpWidget(
      ProviderScope(
        overrides: [httpClientAdapterProvider.overrideWithValue(adapter)],
        child: MaterialApp.router(theme: buildTheme(), routerConfig: router),
      ),
    );
    await settle(tester);

    // Intro card of the bundled story.
    expect(find.text('Last light, one sail'), findsWidgets);
    expect(find.text('Begin →'), findsOneWidget);

    await tester.tap(find.text('Begin →'));
    await settle(tester);
    expect(find.text('Where the light goes'), findsOneWidget);

    // Walk to the end through the right tap zone.
    for (var i = 0; i < 3; i++) {
      await tester.tapAt(const Offset(300, 300));
      await settle(tester);
    }
    expect(find.text('END OF STORY'), findsOneWidget);
    expect(adapter.requests, isEmpty);
  });

  testWidgets('demo_hides_follow_and_inquiry_actions', (tester) async {
    tester.view.physicalSize = phone;
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.reset);
    final router = GoRouter(
      initialLocation: '/demo',
      routes: [
        GoRoute(path: '/', builder: (_, _) => const Text('route:home')),
        GoRoute(path: '/demo', builder: (_, _) => const DemoStoryScreen()),
      ],
    );
    addTearDown(router.dispose);
    await tester.pumpWidget(
      ProviderScope(
        child: MaterialApp.router(theme: buildTheme(), routerConfig: router),
      ),
    );
    await settle(tester);
    await tester.tap(find.text('Begin →'));
    await settle(tester);
    for (var i = 0; i < 3; i++) {
      await tester.tapAt(const Offset(300, 300));
      await settle(tester);
    }
    expect(find.textContaining('Follow'), findsNothing);
    expect(find.textContaining('print'), findsNothing);
  });

  testWidgets('demo_route_is_reachable_from_welcome_and_returns', (
    tester,
  ) async {
    final h = await pumpOnboarding(tester);
    await tester.tap(key('welcome-demo'));
    await settle(tester);
    expect(find.text('Begin →'), findsOneWidget);
    expect(h.api.all.where((r) => r.path.contains('works')), isEmpty);
    // Closing the sample returns to the welcome step, not the feed.
    await tester.tap(find.byKey(const ValueKey('close')).first);
    await settle(tester);
    expect(key('welcome-continue'), findsOneWidget);
  });
}
