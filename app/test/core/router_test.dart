import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';
import 'package:vela_app/core/api/api_providers.dart';
import 'package:vela_app/core/auth/session_store.dart';
import 'package:vela_app/core/require_sign_in.dart';
import 'package:vela_app/core/router.dart';
import 'package:vela_app/features/feed/feed_screen.dart';

import 'support/fakes.dart';

final _signedIn = Session(
  accessToken: 'A',
  refreshToken: 'R',
  accessExpiresAt: DateTime.utc(2030),
).encode();

ProviderContainer _container({
  bool signedIn = false,
  bool needsOnboarding = false,
  SignInSheetLauncher? launcher,
}) {
  final c = ProviderContainer(
    overrides: [
      sessionStorageProvider.overrideWithValue(
        FakeSessionStorage(signedIn ? _signedIn : null),
      ),
      needsOnboardingProvider.overrideWithValue(needsOnboarding),
      httpClientAdapterProvider.overrideWithValue(
        FakeAdapter((_) => jsonResponse(404, errEnvelope('not_found'))),
      ),
      if (launcher != null)
        signInSheetLauncherProvider.overrideWithValue(launcher),
    ],
  );
  addTearDown(c.dispose);
  return c;
}

Future<GoRouter> _pump(WidgetTester tester, ProviderContainer c) async {
  await c.read(sessionProvider.future);
  final router = c.read(routerProvider);
  await tester.pumpWidget(
    UncontrolledProviderScope(
      container: c,
      child: MaterialApp.router(routerConfig: router),
    ),
  );
  await tester.pumpAndSettle();
  return router;
}

void main() {
  testWidgets('guest_can_open_feed_and_story_routes', (tester) async {
    final c = _container();
    final router = await _pump(tester, c);
    expect(find.byType(FeedScreen), findsOneWidget);

    router.go('/story/42');
    await tester.pumpAndSettle();
    expect(find.text("This story isn't available"), findsOneWidget);

    router.go('/artist/ana');
    await tester.pumpAndSettle();
    expect(find.text('Artist not found'), findsOneWidget);

    router.go('/search');
    await tester.pumpAndSettle();
    expect(find.text('route:search'), findsOneWidget);
  });

  for (final path in [
    '/create',
    '/notifications',
    '/settings',
    '/profile/edit',
  ]) {
    testWidgets('guest is sent to feed from $path', (tester) async {
      final router = await _pump(tester, _container());
      router.go(path);
      await tester.pumpAndSettle();
      expect(find.byType(FeedScreen), findsOneWidget);
    });

    testWidgets('signed in user can open $path', (tester) async {
      final router = await _pump(tester, _container(signedIn: true));
      router.go(path);
      await tester.pumpAndSettle();
      expect(find.text('route:${path.substring(1)}'), findsOneWidget);
    });
  }

  testWidgets('signing out on a protected route returns to feed', (
    tester,
  ) async {
    final c = _container(signedIn: true);
    final router = await _pump(tester, c);
    router.go('/settings');
    await tester.pumpAndSettle();

    await c.read(sessionProvider.notifier).clear();
    await tester.pumpAndSettle();

    expect(find.byType(FeedScreen), findsOneWidget);
  });

  testWidgets('not onboarded signed in user is routed to onboarding', (
    tester,
  ) async {
    await _pump(tester, _container(signedIn: true, needsOnboarding: true));
    expect(find.text('route:onboarding'), findsOneWidget);
  });

  test('gating matches sub-paths and trailing slashes', () {
    String? r(String l) => resolveRedirect(
      location: l,
      isSignedIn: false,
      sessionLoading: false,
      needsOnboarding: false,
    );
    for (final l in [
      '/settings/blocked',
      '/create/',
      '/notifications/',
      '/settings',
    ]) {
      expect(r(l), '/', reason: l);
    }
    expect(r('/settingsx'), isNull);
    expect(r('/story/1'), isNull);
  });

  testWidgets('unknown route shows not-found with a link home', (tester) async {
    final router = await _pump(tester, _container());
    router.go('/nope');
    await tester.pumpAndSettle();
    expect(find.text('route:not-found'), findsOneWidget);
    await tester.tap(find.text('Go home'));
    await tester.pumpAndSettle();
    expect(find.byType(FeedScreen), findsOneWidget);
  });

  testWidgets('guest cannot stay on onboarding', (tester) async {
    final router = await _pump(tester, _container());
    router.go('/onboarding');
    await tester.pumpAndSettle();
    expect(find.byType(FeedScreen), findsOneWidget);
  });

  testWidgets('require_sign_in_returns_true_when_session_exists', (
    tester,
  ) async {
    var launched = 0;
    final c = _container(
      signedIn: true,
      launcher: (_) async {
        launched++;
        return true;
      },
    );
    await c.read(sessionProvider.future);
    bool? result;
    await tester.pumpWidget(
      UncontrolledProviderScope(
        container: c,
        child: MaterialApp(
          home: Consumer(
            builder: (context, ref, _) => TextButton(
              onPressed: () async => result = await requireSignIn(context, ref),
              child: const Text('go'),
            ),
          ),
        ),
      ),
    );
    await tester.tap(find.text('go'));
    await tester.pumpAndSettle();

    expect(result, isTrue);
    expect(launched, 0);
  });

  testWidgets(
    'require sign in opens the sheet for guests and returns its result',
    (tester) async {
      for (final outcome in [true, false]) {
        var launched = 0;
        final c = _container(
          launcher: (_) async {
            launched++;
            return outcome;
          },
        );
        await c.read(sessionProvider.future);
        bool? result;
        await tester.pumpWidget(
          UncontrolledProviderScope(
            container: c,
            child: MaterialApp(
              home: Consumer(
                builder: (context, ref, _) => TextButton(
                  onPressed: () async =>
                      result = await requireSignIn(context, ref),
                  child: const Text('go'),
                ),
              ),
            ),
          ),
        );
        await tester.tap(find.text('go'));
        await tester.pumpAndSettle();

        expect(launched, 1);
        expect(result, outcome);
      }
    },
  );

  testWidgets('default launcher returns false', (tester) async {
    final c = _container();
    await c.read(sessionProvider.future);
    late BuildContext ctx;
    await tester.pumpWidget(
      UncontrolledProviderScope(
        container: c,
        child: MaterialApp(
          home: Builder(
            builder: (context) {
              ctx = context;
              return const SizedBox();
            },
          ),
        ),
      ),
    );
    expect(await c.read(signInSheetLauncherProvider)(ctx), isFalse);
  });
}
