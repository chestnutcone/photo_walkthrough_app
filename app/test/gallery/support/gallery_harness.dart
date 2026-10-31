import 'dart:async';
import 'dart:io';
import 'dart:typed_data';
import 'dart:ui' as ui;

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter/rendering.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_riverpod/misc.dart' show Override;
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';
import 'package:vela_app/core/api/api_providers.dart';
import 'package:vela_app/core/auth/session_store.dart';
import 'package:vela_app/core/router.dart';
import 'package:vela_app/core/theme/app_theme.dart';
import 'package:vela_app/features/create/blob_uploader.dart';
import 'package:vela_app/features/create/create_controller.dart';
import 'package:vela_app/features/create/photo_info.dart';
import 'package:vela_app/features/create/photo_picker.dart';
import 'package:vela_app/features/search/recent_searches.dart';
import 'package:vela_app/features/story/story_settings.dart';

import '../../core/support/fakes.dart';
import '../../features/create/support/create_harness.dart'
    show FakeBlobUploader, FakePhotoPicker;
import '../../features/search/support/memory_recents.dart';
import 'gallery_data.dart';

const phoneSize = Size(390, 844);

enum Load { populated, empty, error, loading }

/// What the fake API answers. One instance per screenshot.
class GalleryWorld {
  GalleryWorld({
    this.signedIn = false,
    this.onboarded = true,
    this.suspended = false,
    this.feed = Load.populated,
    this.notifications = Load.populated,
    this.replies = Load.populated,
    this.ownWorks = Load.empty,
    this.artist = Load.populated,
    this.unread = 2,
  });

  final bool signedIn;
  final bool onboarded;
  final bool suspended;
  final Load feed;
  final Load notifications;
  final Load replies;
  final Load ownWorks;
  final Load artist;
  final int unread;

  /// Paths the fake did not know, to spot screens that miss data.
  final unknown = <String>[];

  /// Requests that never answer (for loading states).
  final _hold = Completer<ResponseBody>();

  Map<String, Object?> get me => {
    'id': 'u-a9',
    'profile': {...meProfile, 'onboardingComplete': onboarded},
    'onboardingComplete': onboarded,
    'tier': {'name': 'free', 'maxWorks': 20},
    'usedWorks': 0,
  };

  ResponseBody _list(List<Object?> data) => _json(200, {
    'success': true,
    'data': data,
    'error': null,
    'page': {'nextCursor': null},
  });

  ResponseBody _ok(Object? data) => _json(200, okEnvelope(data));
  ResponseBody _err(int s, String code, [String m = 'Something went wrong']) =>
      jsonResponse(s, errEnvelope(code, m));

  FutureOr<ResponseBody> _byLoad(Load l, ResponseBody Function() ok) =>
      switch (l) {
        Load.populated => ok(),
        Load.empty => _list(const []),
        Load.error => _err(500, 'server_error'),
        Load.loading => _hold.future,
      };

  /// Guests never see `isFollowing` (the server sends null).
  Object? _strip(Object? v) => switch (v) {
    final Map<String, Object?> m => {
      for (final e in m.entries)
        if (e.key != 'isFollowing') e.key: _strip(e.value),
    },
    final List<Object?> l => [for (final x in l) _strip(x)],
    _ => v,
  };

  ResponseBody _json(int status, Object body) =>
      jsonResponse(status, signedIn ? body : _strip(body)!);

  FutureOr<ResponseBody> handle(RequestOptions o) {
    final p = o.path;
    final m = o.method;
    // Only calls with the suspended account's token are refused.
    if (suspended && o.headers['Authorization'] != null) {
      return _err(403, 'account_suspended', 'This account is suspended.');
    }
    if (p == '/v1/me' && m == 'GET') {
      return signedIn ? _ok(me) : _err(401, 'unauthorized');
    }
    if (p == '/v1/feed') {
      return _byLoad(feed, () {
        final tab = o.queryParameters['tab'];
        return _list(tab == 'following' ? [fieldNotes] : feedWorks);
      });
    }
    if (p == '/v1/notifications/unread-count') return _ok({'count': unread});
    if (p == '/v1/notifications' && m == 'GET') {
      return _byLoad(notifications, () => _list(notificationsSample));
    }
    if (p == '/v1/notifications/read') return _ok(null);
    if (p == '/v1/works' && m == 'POST') {
      return _ok({
        'workId': 'wd',
        'uploadUrl': 'https://blob.test/wd?sig=x',
        'expiresAt': '2099-01-01T00:00:00Z',
      });
    }
    if (p == '/v1/works/wd') {
      if (m == 'DELETE') return _ok(null);
      return _ok({...draft, 'status': m == 'GET' ? 'published' : 'draft'});
    }
    if (p == '/v1/works/wd/publish')
      return _ok({...draft, 'status': 'pending'});
    final w = RegExp(r'^/v1/works/(\w+)$').firstMatch(p);
    if (w != null && m == 'GET') {
      final all = [...feedWorks, ...inesWorks];
      final found = all.where((x) => x['id'] == w.group(1));
      return found.isEmpty ? _err(404, 'not_found') : _ok(found.first);
    }
    if (RegExp(r'^/v1/works/\w+/chapters/\d+/replies$').hasMatch(p)) {
      if (m == 'POST') return _ok(reply('r9', meProfile, 'Thank you!'));
      return _byLoad(replies, () => _list(tideReplies));
    }
    if (p == '/v1/artists/suggested') return _list([ines, teo, mara]);
    final a = RegExp(r'^/v1/artists/(\w+)(/\w+)?$').firstMatch(p);
    if (a != null) {
      final handle = a.group(1);
      final sub = a.group(2);
      final matches = [
        ines,
        teo,
        mara,
        meProfile,
      ].where((x) => x['handle'] == handle || x['id'] == handle);
      if (matches.isEmpty) return _err(404, 'not_found');
      final artistJson = matches.first;
      switch (sub) {
        case null:
          if (artist == Load.error) return _err(500, 'server_error');
          if (artist == Load.empty) return _err(404, 'not_found');
          if (artist == Load.loading) return _hold.future;
          return _ok(artistJson);
        case '/works':
          return handle == 'you'
              ? _byLoad(ownWorks, () => _list(inesWorks))
              : _list(inesWorks);
        case '/followers':
          return _list([teo, mara, meProfile]);
        case '/follow':
          return _ok(null);
        case '/inquiries':
          return _ok({
            'id': 'q1',
            'artistId': 'a1',
            'type': 'Commission',
            'message': 'Hi',
            'createdAt': '2026-10-30T10:00:00Z',
          });
      }
    }
    if (p == '/v1/search') {
      final q = (o.queryParameters['q'] as String? ?? '').toLowerCase();
      final type = o.queryParameters['type'] ?? 'artists';
      final artists = [
        ines,
        teo,
        mara,
      ].where((x) => '${x['displayName']}'.toLowerCase().contains(q)).toList();
      final stories = [
        ...feedWorks,
        ropes,
        saguaro,
      ].where((x) => '${x['title']}'.toLowerCase().contains(q)).toList();
      return _json(200, {
        'success': true,
        'data': {
          'artists': type == 'artists' ? artists : [],
          'stories': type == 'stories' ? stories : [],
        },
        'error': null,
        'page': {'nextCursor': null},
      });
    }
    if (p == '/v1/me/onboarding') return _ok(me);
    if (p == '/v1/auth/email/request') return _ok(null);
    if (p == '/v1/me/export' && m == 'GET') return _err(404, 'not_found');
    if (p == '/v1/reports') return _ok(null);
    unknown.add('$m $p');
    return _err(404, 'not_found');
  }
}

final List<Object?> notificationsSample = notifications;

final Map<String, Object?> draft = {
  ...lowTide,
  'id': 'wd',
  'author': summary(meProfile),
  'isOwner': true,
  'chapters': <Object?>[],
};

/// Photographs by URL, decoded once per test run.
final _photos = <String, MemoryImage>{};
ImageProvider galleryImage(String url) => _photos.putIfAbsent(url, () {
  final id = RegExp(r'/w(\d)').firstMatch(url)?.group(1);
  return MemoryImage(samplePhoto(int.tryParse(id ?? '') ?? url.length));
});

final _session = Session(
  accessToken: 'A',
  refreshToken: 'R',
  accessExpiresAt: DateTime.utc(2030),
).encode();

final galleryBoundary = GlobalKey();

class Gallery {
  Gallery(this.tester, this.world, this.container, this.picker);
  final WidgetTester tester;
  final GalleryWorld world;
  final ProviderContainer container;
  final FakePhotoPicker picker;

  GoRouter get router => container.read(routerProvider);

  Future<void> go(String location) async {
    router.go(location);
    await settle();
  }

  Future<void> push(String location) async {
    unawaited(router.push(location));
    await settle();
  }

  /// Lets network fakes, redirects, animations and image decoding finish.
  Future<void> settle({bool animations = true}) async {
    for (var i = 0; i < 3; i++) {
      await tester.pump(const Duration(milliseconds: 50));
      await tester.runAsync(
        () => Future<void>.delayed(const Duration(milliseconds: 20)),
      );
    }
    if (animations) {
      await tester.pumpAndSettle(
        const Duration(milliseconds: 100),
        EnginePhase.sendSemanticsUpdate,
        const Duration(seconds: 10),
      );
    } else {
      await tester.pump(const Duration(milliseconds: 400));
    }
    await tester.runAsync(
      () => Future<void>.delayed(const Duration(milliseconds: 20)),
    );
    await tester.pump();
  }

  Future<void> tap(Finder f) async {
    await tester.ensureVisible(f.first);
    await tester.pumpAndSettle();
    await tester.tap(f.first);
    await settle();
  }
}

/// Pumps the real app router against [world] at phone size.
Future<Gallery> pumpGallery(
  WidgetTester tester,
  GalleryWorld world, {
  double textScale = 1.0,
  Size size = phoneSize,
  List<Override> overrides = const [],
}) async {
  tester.view.physicalSize = size;
  tester.view.devicePixelRatio = 1;
  tester.platformDispatcher.textScaleFactorTestValue = textScale;
  addTearDown(tester.view.reset);
  addTearDown(tester.platformDispatcher.clearTextScaleFactorTestValue);
  tester.binding.defaultBinaryMessenger.setMockMethodCallHandler(
    SystemChannels.platform,
    (_) async => null,
  );
  addTearDown(
    () => tester.binding.defaultBinaryMessenger.setMockMethodCallHandler(
      SystemChannels.platform,
      null,
    ),
  );

  final picker = FakePhotoPicker(PickedPhoto(samplePhoto(1, w: 480, h: 600)));
  await tester.pumpWidget(
    ProviderScope(
      overrides: [
        sessionStorageProvider.overrideWithValue(
          FakeSessionStorage(world.signedIn ? _session : null),
        ),
        httpClientAdapterProvider.overrideWithValue(FakeAdapter(world.handle)),
        storyImageProviderFactoryProvider.overrideWithValue(galleryImage),
        storyAutoplayDefaultProvider.overrideWithValue(false),
        recentSearchesStoreProvider.overrideWithValue(
          MemoryRecentsStore(['harbour', 'Mara']),
        ),
        photoPickerProvider.overrideWithValue(picker),
        blobUploaderProvider.overrideWithValue(FakeBlobUploader()),
        photoNormalizerProvider.overrideWithValue((b) async => b),
        backgroundRetryDelayProvider.overrideWithValue((_) async {}),
        pollDelayProvider.overrideWithValue((_) async {}),
        ...overrides,
      ],
      child: RepaintBoundary(
        key: galleryBoundary,
        child: Consumer(
          builder: (context, ref, _) => MaterialApp.router(
            debugShowCheckedModeBanner: false,
            theme: buildTheme(),
            routerConfig: ref.watch(routerProvider),
          ),
        ),
      ),
    ),
  );
  final container = ProviderScope.containerOf(
    tester.element(find.byType(MaterialApp)),
  );
  final g = Gallery(tester, world, container, picker);
  await g.settle();
  return g;
}

/// Writes the current frame to [path] (PNG, 1 px per logical pixel).
Future<void> captureTo(WidgetTester tester, String path) async {
  final boundary = tester.renderObject<RenderRepaintBoundary>(
    find.byKey(galleryBoundary),
  );
  final bytes = await tester.runAsync<Uint8List>(() async {
    final image = await boundary.toImage(pixelRatio: 1);
    final data = await image.toByteData(format: ui.ImageByteFormat.png);
    image.dispose();
    return data!.buffer.asUint8List();
  });
  final f = File(path);
  f.parent.createSync(recursive: true);
  f.writeAsBytesSync(bytes!);
}
