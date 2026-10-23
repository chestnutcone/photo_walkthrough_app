import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_riverpod/misc.dart' show Override;
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';
import 'package:vela_app/core/api/api_providers.dart';
import 'package:vela_app/core/auth/session_store.dart';
import 'package:vela_app/core/require_sign_in.dart';
import 'package:vela_app/core/theme/app_theme.dart';
import 'package:vela_app/features/search/recent_searches.dart';
import 'package:vela_app/features/search/search_screen.dart';
import 'package:vela_app/features/story/story_settings.dart';

import '../../../core/support/fakes.dart';
import 'fake_search_api.dart';
import 'memory_recents.dart';

final _png = base64Decode(
  'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==',
);

class SearchHarness {
  SearchHarness(this.api, this.router, this.recents, this.signInLaunches);
  final FakeSearchApi api;
  final GoRouter router;
  final MemoryRecentsStore recents;
  final List<int> signInLaunches;
}

Future<SearchHarness> pumpSearch(
  WidgetTester tester, {
  FakeSearchApi? api,
  bool signedIn = false,
  bool signInSucceeds = false,
  MemoryRecentsStore? recents,
  List<Override> overrides = const [],
  Size size = const Size(390, 844),
}) async {
  tester.view.physicalSize = size;
  tester.view.devicePixelRatio = 1;
  addTearDown(tester.view.reset);

  final fake = api ?? FakeSearchApi();
  final store = recents ?? MemoryRecentsStore();
  final launches = <int>[];
  final session = signedIn
      ? Session(
          accessToken: 'A',
          refreshToken: 'R',
          accessExpiresAt: DateTime.utc(2030),
        ).encode()
      : null;
  final router = GoRouter(
    initialLocation: '/search',
    routes: [
      GoRoute(path: '/', builder: (_, _) => const Text('route:feed')),
      GoRoute(path: '/search', builder: (_, _) => const SearchScreen()),
      GoRoute(
        path: '/artist/:handle',
        builder: (_, s) => Text('route:artist/${s.pathParameters['handle']}'),
      ),
      GoRoute(
        path: '/story/:id',
        builder: (_, s) => Text('route:story/${s.pathParameters['id']}'),
      ),
    ],
  );
  addTearDown(router.dispose);
  await tester.pumpWidget(
    ProviderScope(
      overrides: [
        apiProvider.overrideWithValue(fake),
        sessionStorageProvider.overrideWithValue(FakeSessionStorage(session)),
        recentSearchesStoreProvider.overrideWithValue(store),
        storyImageProviderFactoryProvider.overrideWithValue(
          (_) => MemoryImage(_png),
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
  return SearchHarness(fake, router, store, launches);
}

/// Types [text] and lets the 300 ms debounce fire.
Future<void> typeQuery(WidgetTester tester, String text) async {
  await tester.enterText(find.byKey(const ValueKey('search-field')), text);
  await tester.pump(const Duration(milliseconds: 300));
  await tester.pump();
}
