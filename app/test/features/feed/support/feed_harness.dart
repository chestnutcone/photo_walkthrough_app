import 'dart:async';
import 'dart:convert';
import 'dart:typed_data';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_riverpod/misc.dart' show Override;
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';
import 'package:vela_app/core/api/api_providers.dart';
import 'package:vela_app/core/auth/session_store.dart';
import 'package:vela_app/core/require_sign_in.dart';
import 'package:vela_app/core/theme/app_theme.dart';
import 'package:vela_app/features/feed/feed_screen.dart';
import 'package:vela_app/features/story/story_settings.dart';

import '../../../core/support/fakes.dart';

const phone = Size(390, 844);

final Uint8List _png = base64Decode(
  'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==',
);

Map<String, Object?> feedWork(
  String id, {
  String authorId = 'a1',
  String handle = 'ines',
  String name = 'Ines Alvarez',
  bool? isFollowing = false,
  int chapters = 3,
}) => {
  'id': id,
  'author': {
    'id': authorId,
    'handle': handle,
    'displayName': name,
    'isFollowing': ?isFollowing,
  },
  'title': 'Title $id',
  'altText': 'Alt for $id',
  'isOwner': false,
  'thumbUrl': 'https://img.test/$id-thumb.jpg',
  'feedUrl': 'https://img.test/$id-feed.jpg',
  'imageWidth': 3000,
  'imageHeight': 4000,
  'chapters': [
    for (var i = 0; i < chapters; i++)
      {
        'x': 0.2 + 0.2 * i,
        'y': 0.3 + 0.1 * i,
        'title': 'Detail ${i + 1}',
        'body': 'Body',
        'replyCount': i == 1 ? 2 : 0,
      },
  ],
  'createdAt': '2026-10-01T10:00:00Z',
  'publishedAt': '2026-10-02T10:00:00Z',
  'shareUrl': 'https://vela.example/s/$id',
};

Map<String, Object?> feedPage(
  List<Map<String, Object?>> works, {
  String? next,
}) => {
  'success': true,
  'data': works,
  'error': null,
  'page': {'nextCursor': next},
};

class FeedHarness {
  FeedHarness(this.adapter, this.router, this.signInLaunches);
  final FakeAdapter adapter;
  final GoRouter router;
  final List<int> signInLaunches;

  List<RequestOptions> feedRequests([String? tab]) => adapter.requests
      .where(
        (r) =>
            r.path == '/v1/feed' &&
            (tab == null || r.queryParameters['tab'] == tab),
      )
      .toList();

  List<RequestOptions> followRequests() =>
      adapter.requests.where((r) => r.path.endsWith('/follow')).toList();
}

Future<FeedHarness> pumpFeed(
  WidgetTester tester, {
  required FutureOr<ResponseBody> Function(RequestOptions) feed,
  FutureOr<ResponseBody> Function(RequestOptions)? follow,
  int unread = 0,
  bool signedIn = false,
  bool signInSucceeds = false,
  List<Override> overrides = const [],
}) async {
  tester.view.physicalSize = phone;
  tester.view.devicePixelRatio = 1;
  addTearDown(tester.view.reset);

  final launches = <int>[];
  final adapter = FakeAdapter((o) {
    if (o.path == '/v1/feed') return feed(o);
    if (o.path.endsWith('/follow')) {
      return follow?.call(o) ?? jsonResponse(200, okEnvelope(null));
    }
    if (o.path == '/v1/notifications/unread-count') {
      return jsonResponse(200, okEnvelope({'count': unread}));
    }
    return jsonResponse(404, errEnvelope('not_found'));
  });
  final session = signedIn
      ? Session(
          accessToken: 'A',
          refreshToken: 'R',
          accessExpiresAt: DateTime.utc(2030),
        ).encode()
      : null;

  final router = GoRouter(
    routes: [
      GoRoute(path: '/', builder: (_, _) => const FeedScreen()),
      GoRoute(
        path: '/story/:id',
        builder: (_, s) => Text('route:story/${s.pathParameters['id']}'),
      ),
      GoRoute(
        path: '/artist/:handle',
        builder: (_, s) => Text('route:artist/${s.pathParameters['handle']}'),
      ),
      GoRoute(path: '/search', builder: (_, _) => const Text('route:search')),
      GoRoute(
        path: '/notifications',
        builder: (_, _) => const Text('route:notifications'),
      ),
    ],
  );
  addTearDown(router.dispose);

  await tester.pumpWidget(
    ProviderScope(
      overrides: [
        sessionStorageProvider.overrideWithValue(FakeSessionStorage(session)),
        httpClientAdapterProvider.overrideWithValue(adapter),
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
  await tester.pumpAndSettle();
  return FeedHarness(adapter, router, launches);
}

Finder card(String id) => find.byKey(ValueKey('post-card-$id'));
Finder followPill(String id) => find.byKey(ValueKey('follow-pill-$id'));

/// Text inside the follow pills of [workIds], to tell pills apart from the
/// "Following" tab label.
Finder pillText(List<String> workIds, String text) => find.descendant(
  of: find.byWidgetPredicate(
    (w) =>
        w.key is ValueKey<String> &&
        workIds.any((id) => w.key == ValueKey('follow-pill-$id')),
  ),
  matching: find.text(text),
);
