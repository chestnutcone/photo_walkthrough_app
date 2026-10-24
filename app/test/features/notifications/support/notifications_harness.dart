import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_riverpod/misc.dart' show Override;
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';
import 'package:vela_app/core/api/api_providers.dart';
import 'package:vela_app/core/auth/session_store.dart';
import 'package:vela_app/core/require_sign_in.dart';
import 'package:vela_app/core/theme/app_theme.dart';
import 'package:vela_app/features/notifications/notifications_screen.dart';
import 'package:vela_app/features/notifications/unread_count_provider.dart';
import 'package:vela_app/features/story/story_settings.dart';

import '../../../core/support/fakes.dart';
import '../../story/support/story_harness.dart' show transparentPng;
import 'fake_notifications_api.dart';

class NotificationsHarness {
  NotificationsHarness(
    this.api,
    this.router,
    this.signInLaunches,
    this.storage,
  );
  final FakeNotificationsApi api;
  final GoRouter router;
  final List<int> signInLaunches;
  final FakeSessionStorage storage;

  ProviderContainer get container => ProviderScope.containerOf(
    // The screen is on the stack in every test that calls this.
    _context!,
  );
  BuildContext? _context;
}

Future<NotificationsHarness> pumpNotifications(
  WidgetTester tester, {
  FakeNotificationsApi? api,
  bool signedIn = true,
  bool signInSucceeds = false,
  List<Override> overrides = const [],
}) async {
  tester.view.physicalSize = const Size(390, 844);
  tester.view.devicePixelRatio = 1;
  addTearDown(tester.view.reset);

  final fake = api ?? FakeNotificationsApi();
  final launches = <int>[];
  final storage = FakeSessionStorage(
    signedIn
        ? Session(
            accessToken: 'A',
            refreshToken: 'R',
            accessExpiresAt: DateTime.utc(2030),
          ).encode()
        : null,
  );
  late NotificationsHarness harness;
  final router = GoRouter(
    initialLocation: '/notifications',
    routes: [
      GoRoute(path: '/', builder: (_, _) => const Text('route:feed')),
      GoRoute(
        path: '/notifications',
        builder: (context, _) {
          harness._context = context;
          return const NotificationsScreen();
        },
      ),
      GoRoute(
        path: '/artist/:handle',
        builder: (_, s) => Text('route:${s.uri}'),
      ),
      GoRoute(path: '/story/:id', builder: (_, s) => Text('route:${s.uri}')),
    ],
  );
  addTearDown(router.dispose);
  harness = NotificationsHarness(fake, router, launches, storage);
  await tester.pumpWidget(
    ProviderScope(
      overrides: [
        apiProvider.overrideWithValue(fake),
        sessionStorageProvider.overrideWithValue(storage),
        clockProvider.overrideWithValue(() => DateTime.utc(2026, 10, 22, 12)),
        storyImageProviderFactoryProvider.overrideWithValue(
          (_) => MemoryImage(transparentPng),
        ),
        signInSheetLauncherProvider.overrideWithValue((_) async {
          launches.add(1);
          return signInSucceeds;
        }),
        ...overrides,
      ],
      child: MaterialApp.router(theme: buildTheme(), routerConfig: router),
    ),
  );
  await tester.pump();
  await tester.pump();
  return harness;
}

/// Lets the screen's first request finish with [items].
Future<void> answerList(
  WidgetTester tester,
  FakeNotificationsApi api,
  List<Map<String, Object?>> items, {
  int call = 0,
  String? next,
}) async {
  api.listCalls[call].reply(items, next: next);
  await tester.pump();
  await tester.pump();
}

/// Keeps `unreadCountProvider` alive like the feed's bell would.
ProviderSubscription<AsyncValue<int>> watchBadge(NotificationsHarness h) =>
    h.container.listen(unreadCountProvider, (_, _) {});
