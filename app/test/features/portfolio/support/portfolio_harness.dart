import 'dart:async';
import 'dart:convert';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_riverpod/misc.dart' show Override;
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';
import 'package:vela_app/core/api/api_providers.dart';
import 'package:vela_app/core/auth/session_store.dart';
import 'package:vela_app/core/require_sign_in.dart';
import 'package:vela_app/core/theme/app_theme.dart';
import 'package:vela_app/features/portfolio/portfolio_screen.dart';
import 'package:vela_app/features/story/story_settings.dart';

import '../../../core/support/fakes.dart';
import '../../feed/support/feed_harness.dart' show feedWork;

final Uint8List _png = base64Decode(
  'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==',
);

const loginEmail = 'login-secret@example.com';

Map<String, Object?> profileJson({
  String id = 'a1',
  String handle = 'ines',
  String name = 'Ines Alvarez',
  bool? isFollowing = false,
  int followers = 12,
  String? publicEmail,
  List<Map<String, String>>? services,
  List<Map<String, String>>? shows,
  String? replyTime = '2 days',
}) => {
  'id': id,
  'handle': handle,
  'displayName': name,
  'bio': 'Light and salt.',
  'followerCount': followers,
  'followingCount': 4,
  'workCount': 3,
  'chapterCount': 9,
  'isFollowing': ?isFollowing,
  'onboardingComplete': true,
  'city': 'Lisbon',
  'discipline': 'Photographer',
  'availability': 'Open for commissions',
  'publicEmail': ?publicEmail,
  'services': ?services,
  'shows': ?shows,
  'replyTime': ?replyTime,
};

Map<String, Object?> meJson({
  String handle = 'me',
  String id = 'a9',
  int used = 0,
  int max = 20,
}) => {
  'id': 'u-$id',
  'profile': profileJson(id: id, handle: handle, isFollowing: null),
  'onboardingComplete': true,
  'tier': {'name': 'free', 'maxWorks': max},
  'usedWorks': used,
};

Map<String, Object?> listPage(List<Object?> data, {String? next}) => {
  'success': true,
  'data': data,
  'error': null,
  'page': {'nextCursor': next},
};

class PortfolioHarness {
  PortfolioHarness(this.adapter, this.router, this.signInLaunches, this.clips);
  final FakeAdapter adapter;
  final GoRouter router;
  final List<int> signInLaunches;
  final List<String> clips;

  List<RequestOptions> requestsTo(String suffix) =>
      adapter.requests.where((r) => r.path.endsWith(suffix)).toList();
}

typedef Responder = FutureOr<ResponseBody> Function(RequestOptions);

/// Pumps the portfolio for [handle] on a tall viewport (no scrolling needed).
Future<PortfolioHarness> pumpPortfolio(
  WidgetTester tester, {
  String handle = 'ines',
  Responder? artist,
  Responder? works,
  Responder? followers,
  Responder? inquiry,
  Responder? follow,
  Map<String, Object?>? me,
  bool signInSucceeds = false,
  List<Override> overrides = const [],
}) async {
  tester.view.physicalSize = const Size(390, 2400);
  tester.view.devicePixelRatio = 1;
  addTearDown(tester.view.reset);

  final clips = <String>[];
  tester.binding.defaultBinaryMessenger.setMockMethodCallHandler(
    SystemChannels.platform,
    (call) async {
      if (call.method == 'Clipboard.setData') {
        clips.add((call.arguments as Map)['text'] as String);
      }
      return null;
    },
  );
  addTearDown(
    () => tester.binding.defaultBinaryMessenger.setMockMethodCallHandler(
      SystemChannels.platform,
      null,
    ),
  );

  final launches = <int>[];
  final adapter = FakeAdapter((o) {
    final p = o.path;
    if (p == '/v1/me') {
      return me == null
          ? jsonResponse(401, errEnvelope('unauthorized'))
          : jsonResponse(200, okEnvelope(me));
    }
    if (p.endsWith('/works') && p.startsWith('/v1/artists/')) {
      return works?.call(o) ?? jsonResponse(200, listPage([]));
    }
    if (p.endsWith('/followers')) {
      return followers?.call(o) ?? jsonResponse(200, listPage([]));
    }
    if (p.endsWith('/inquiries')) {
      return inquiry?.call(o) ??
          jsonResponse(
            200,
            okEnvelope({
              'type': 'Commission',
              'id': 'i1',
              'artistId': 'a1',
              'message': 'm',
              'createdAt': '2026-10-01T10:00:00Z',
            }),
          );
    }
    if (p.endsWith('/follow')) {
      return follow?.call(o) ?? jsonResponse(200, okEnvelope(null));
    }
    if (p.startsWith('/v1/artists/')) {
      return artist?.call(o) ??
          jsonResponse(200, okEnvelope(profileJson(handle: handle)));
    }
    if (p.startsWith('/v1/works/')) {
      return jsonResponse(
        200,
        okEnvelope(workJson(p.substring('/v1/works/'.length))),
      );
    }
    return jsonResponse(404, errEnvelope('not_found'));
  });
  final session = me == null
      ? null
      : Session(
          accessToken: 'A',
          refreshToken: 'R',
          accessExpiresAt: DateTime.utc(2030),
        ).encode();

  final router = GoRouter(
    initialLocation: '/artist/$handle',
    routes: [
      GoRoute(path: '/', builder: (_, _) => const Text('route:feed')),
      GoRoute(
        path: '/artist/:handle',
        builder: (_, s) => PortfolioScreen(handle: s.pathParameters['handle']!),
      ),
      GoRoute(
        path: '/story/:id',
        builder: (_, s) => Text('route:story/${s.pathParameters['id']}'),
      ),
      GoRoute(path: '/create', builder: (_, _) => const Text('route:create')),
      GoRoute(
        path: '/profile/edit',
        builder: (_, _) => const Text('route:profile-edit'),
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
  return PortfolioHarness(adapter, router, launches, clips);
}

Map<String, Object?> workJson(String id, {int chapters = 3}) =>
    feedWork(id, chapters: chapters);
