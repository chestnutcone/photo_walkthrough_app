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
import 'package:vela_app/features/story/story_screen.dart';
import 'package:vela_app/features/story/story_settings.dart';

import '../../../core/support/fakes.dart';

const phone = Size(390, 844);

/// 1x1 transparent PNG, for tests that build their own app.
Uint8List get transparentPng => _png;

/// 1x1 transparent PNG.
final Uint8List _png = base64Decode(
  'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==',
);

Map<String, Object?> chapterJson(
  int i, {
  double x = 0.5,
  double y = 0.5,
  double? zoom = 2.4,
  int? replyCount,
}) => {
  'x': x,
  'y': y,
  'zoom': ?zoom,
  'title': 'Detail ${i + 1}',
  'body': 'Body of detail ${i + 1}',
  'meta': '85mm · f/4',
  'replyCount': ?replyCount,
};

Map<String, Object?> workJson({
  List<Map<String, Object?>>? chapters,
  bool isOwner = false,
  bool? isFollowing = false,
  int? imageWidth,
  int? imageHeight,
  String? altText = 'A lighthouse at dusk',
}) => {
  'id': 'w1',
  'author': {
    'id': 'a1',
    'handle': 'ines',
    'displayName': 'Ines Alvarez',
    'isFollowing': ?isFollowing,
  },
  'title': 'Salt and Rope',
  'intro': 'A morning on the quay.',
  'altText': altText,
  'isOwner': isOwner,
  'imageUrl': 'https://img.test/full.jpg',
  'thumbUrl': 'https://img.test/thumb.jpg',
  'imageWidth': ?imageWidth,
  'imageHeight': ?imageHeight,
  'chapters':
      chapters ??
      [
        chapterJson(0, x: 0.3, y: 0.4),
        chapterJson(1, x: 0.6, y: 0.5, zoom: null, replyCount: 2),
        chapterJson(2, x: 0.8, y: 0.7),
      ],
  'createdAt': '2026-10-01T10:00:00Z',
  'publishedAt': '2026-10-02T10:00:00Z',
  'shareUrl': 'https://vela.example/s/w1',
};

class StoryHarness {
  StoryHarness(this.adapter, this.router);
  final FakeAdapter adapter;
  final GoRouter router;
}

Future<StoryHarness> pumpStory(
  WidgetTester tester, {
  Map<String, Object?>? work,
  int workStatus = 200,
  String errorCode = 'not_found',
  bool readOnly = false,
  bool signedIn = false,
  bool autoplay = false,
  bool signInSucceeds = false,
  List<Override> overrides = const [],
  bool settle = true,
  int followStatus = 200,
  StoryImageProviderFactory? imageFactory,
  bool networkError = false,
  int? initialChapter,
  bool openReplies = false,
}) async {
  tester.view.physicalSize = phone;
  tester.view.devicePixelRatio = 1;
  addTearDown(tester.view.reset);

  final adapter = FakeAdapter((o) {
    if (o.path == '/v1/works/w1') {
      if (networkError) {
        throw DioException.connectionError(
          requestOptions: o,
          reason: 'offline',
        );
      }
      return workStatus == 200
          ? jsonResponse(200, okEnvelope(work ?? workJson()))
          : jsonResponse(workStatus, errEnvelope(errorCode));
    }
    if (o.path == '/v1/artists/a1/follow') {
      return followStatus == 200
          ? jsonResponse(200, okEnvelope(null))
          : jsonResponse(followStatus, errEnvelope('server_error'));
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
    initialLocation: '/story/w1',
    routes: [
      GoRoute(path: '/', builder: (_, _) => const Text('route:feed')),
      GoRoute(
        path: '/story/:id',
        builder: (_, s) => StoryScreen(
          workId: s.pathParameters['id']!,
          readOnly: readOnly,
          initialChapter: initialChapter,
          openReplies: openReplies,
        ),
      ),
      GoRoute(
        path: '/artist/:handle',
        builder: (_, s) => Text('route:artist/${s.pathParameters['handle']}'),
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
          imageFactory ?? (_) => MemoryImage(_png),
        ),
        storyAutoplayDefaultProvider.overrideWithValue(autoplay),
        signInSheetLauncherProvider.overrideWithValue(
          (_) async => signInSucceeds,
        ),
        ...overrides,
      ],
      child: MaterialApp.router(theme: buildTheme(), routerConfig: router),
    ),
  );
  if (settle) await tester.pumpAndSettle();
  return StoryHarness(adapter, router);
}

/// Pin buttons are keyed `pin-<index>`.
Finder pin(int i) => find.byKey(ValueKey('pin-$i'));

/// Shorthand for the visible chapter kicker, e.g. `CHAPTER 02 / 03`.
Finder kicker(int chapter, int total) => find.text(
  'CHAPTER ${chapter.toString().padLeft(2, '0')} / '
  '${total.toString().padLeft(2, '0')}',
);

/// An [ImageProvider] whose load always fails.
class FailingImage extends ImageProvider<FailingImage> {
  FailingImage(this.url);
  final String url;

  @override
  Future<FailingImage> obtainKey(ImageConfiguration configuration) =>
      Future.value(this);

  @override
  ImageStreamCompleter loadImage(
    FailingImage key,
    ImageDecoderCallback decode,
  ) => OneFrameImageStreamCompleter(
    Future<ImageInfo>.error(StateError('boom'), StackTrace.empty),
  );
}
