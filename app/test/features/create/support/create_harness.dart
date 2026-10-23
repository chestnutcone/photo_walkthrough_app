import 'dart:async';
import 'dart:convert';
import 'dart:typed_data';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_riverpod/misc.dart' show Override;
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';
import 'package:image/image.dart' as img;
import 'package:vela_app/core/api/api_providers.dart';
import 'package:vela_app/core/auth/session_store.dart';
import 'package:vela_app/core/theme/app_theme.dart';
import 'package:vela_app/features/create/blob_uploader.dart';
import 'package:vela_app/features/create/create_controller.dart';
import 'package:vela_app/features/create/create_screen.dart';
import 'package:vela_app/features/create/photo_info.dart';
import 'package:vela_app/features/create/photo_picker.dart';

import '../../../core/support/fakes.dart';
import '../../portfolio/support/portfolio_harness.dart' show meJson;
import '../../story/support/story_harness.dart' show workJson;

const phone = Size(390, 844);

/// A real 1x1 PNG.
final Uint8List tinyPng = base64Decode(
  'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==',
);

/// A JPEG whose stored pixels are [w] x [h] with a 2x2 red block at
/// ([bx], [by]) and the given EXIF [orientation].
Uint8List jpegWithOrientation({
  int w = 40,
  int h = 30,
  int orientation = 6,
  int bx = 4,
  int by = 2,
  bool gps = false,
}) {
  final image = img.Image(width: w, height: h);
  img.fill(image, color: img.ColorRgb8(20, 20, 20));
  img.fillRect(
    image,
    x1: bx,
    y1: by,
    x2: bx + 1,
    y2: by + 1,
    color: img.ColorRgb8(255, 0, 0),
  );
  image.exif.imageIfd.orientation = orientation;
  if (gps) image.exif.gpsIfd.gpsLatitude = 12.5;
  return Uint8List.fromList(img.encodeJpg(image, quality: 100));
}

class FakePhotoPicker implements PhotoPicker {
  FakePhotoPicker(this.result);
  PickedPhoto? result;
  int calls = 0;
  Object? error;

  @override
  Future<PickedPhoto?> pick(PhotoSource source) async {
    calls++;
    if (error != null) throw error!;
    return result;
  }
}

class FakeBlobUploader implements BlobUploader {
  final List<({Uri url, int length, String contentType, Uint8List bytes})>
  uploads = [];
  Object? failWith;

  @override
  Future<void> upload({
    required Uri url,
    required Uint8List bytes,
    required String contentType,
  }) async {
    if (failWith != null) throw failWith!;
    uploads.add((
      url: url,
      length: bytes.length,
      contentType: contentType,
      bytes: bytes,
    ));
  }
}

Map<String, Object?> draftJson(String status) => {
  ...workJson(isOwner: true, chapters: const []),
  'id': 'w1',
  'status': status,
};

class CreateHarness {
  CreateHarness({
    required this.adapter,
    required this.router,
    required this.picker,
    required this.uploader,
    required this.delays,
    required this.container,
  });
  final FakeAdapter adapter;
  final GoRouter router;
  final FakePhotoPicker picker;
  final FakeBlobUploader uploader;
  final List<Duration> delays;
  final ProviderContainer container;

  List<RequestOptions> calls(String method, String path) => adapter.requests
      .where((r) => r.method == method && r.path == path)
      .toList();
}

/// [statuses] are returned by successive `GET /v1/works/w1`.
Future<CreateHarness> pumpCreate(
  WidgetTester tester, {
  Uint8List? photo,
  List<String> statuses = const ['published'],
  int createStatus = 200,
  String createCode = 'quota_exceeded',
  String createMessage = 'Story limit reached (20).',
  int used = 20,
  int max = 20,
  int updateStatus = 200,
  String updateCode = 'validation_failed',
  int publishStatus = 200,
  String publishCode = 'draft_expired',
  bool signedIn = true,
  Future<void> Function(Duration)? pollDelay,
  FutureOr<ResponseBody>? Function(RequestOptions)? intercept,
  List<Override> overrides = const [],
}) async {
  tester.view.physicalSize = phone;
  tester.view.devicePixelRatio = 1;
  addTearDown(tester.view.reset);

  var getCount = 0;
  final adapter = FakeAdapter((o) {
    final early = intercept?.call(o);
    if (early != null) return early;
    final key = '${o.method} ${o.path}';
    switch (key) {
      case 'POST /v1/works':
        return createStatus == 200
            ? jsonResponse(
                200,
                okEnvelope({
                  'workId': 'w1',
                  'uploadUrl': 'https://blob.test/w1?sig=SECRET',
                  'expiresAt': '2099-01-01T00:00:00Z',
                }),
              )
            : jsonResponse(
                createStatus,
                errEnvelope(createCode, createMessage),
              );
      case 'GET /v1/me':
        return jsonResponse(200, okEnvelope(meJson(used: used, max: max)));
      case 'PUT /v1/works/w1':
        return updateStatus == 200
            ? jsonResponse(200, okEnvelope(draftJson('draft')))
            : jsonResponse(updateStatus, errEnvelope(updateCode));
      case 'POST /v1/works/w1/publish':
        return publishStatus == 200
            ? jsonResponse(200, okEnvelope(draftJson('pending')))
            : jsonResponse(publishStatus, errEnvelope(publishCode));
      case 'GET /v1/works/w1':
        final s = statuses[getCount.clamp(0, statuses.length - 1)];
        getCount++;
        return jsonResponse(200, okEnvelope(draftJson(s)));
      case 'DELETE /v1/works/w1':
        return jsonResponse(200, okEnvelope(null));
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
  final picker = FakePhotoPicker(PickedPhoto(photo ?? tinyPng));
  final uploader = FakeBlobUploader();
  final delays = <Duration>[];

  final router = GoRouter(
    initialLocation: '/',
    routes: [
      GoRoute(
        path: '/',
        builder: (_, _) => const Scaffold(body: Text('route:feed')),
      ),
      GoRoute(path: '/create', builder: (_, _) => const CreateScreen()),
      GoRoute(
        path: '/story/:id',
        builder: (_, s) => Text('route:story/${s.pathParameters['id']}'),
      ),
    ],
  );
  addTearDown(router.dispose);

  final key = GlobalKey();
  await tester.pumpWidget(
    ProviderScope(
      key: key,
      overrides: [
        sessionStorageProvider.overrideWithValue(FakeSessionStorage(session)),
        httpClientAdapterProvider.overrideWithValue(adapter),
        photoPickerProvider.overrideWithValue(picker),
        blobUploaderProvider.overrideWithValue(uploader),
        photoNormalizerProvider.overrideWithValue(
          (b) async => bakePhotoOrientation(b),
        ),
        backgroundRetryDelayProvider.overrideWithValue((_) async {}),
        pollDelayProvider.overrideWithValue((d) {
          delays.add(d);
          return pollDelay?.call(d) ?? Future<void>.value();
        }),
        ...overrides,
      ],
      child: MaterialApp.router(theme: buildTheme(), routerConfig: router),
    ),
  );
  await tester.pumpAndSettle();
  unawaited(router.push('/create'));
  await tester.pumpAndSettle();
  final container = ProviderScope.containerOf(
    tester.element(find.byType(CreateScreen)),
  );
  return CreateHarness(
    adapter: adapter,
    router: router,
    picker: picker,
    uploader: uploader,
    delays: delays,
    container: container,
  );
}

Finder get photoBox => find.byKey(const ValueKey('photo-box'));

/// Taps the photo at [fx], [fy] of the box the author sees.
Future<void> tapPhotoAt(WidgetTester tester, double fx, double fy) async {
  final rect = tester.getRect(photoBox);
  await tester.tapAt(
    Offset(rect.left + fx * rect.width, rect.top + fy * rect.height),
  );
  await tester.pumpAndSettle();
}

Future<void> pickAndAnnotate(WidgetTester tester) async {
  await tester.tap(find.byKey(const ValueKey('pick-library')));
  await tester.pumpAndSettle();
}

Future<void> addChapter(
  WidgetTester tester, {
  required String title,
  double fx = 0.5,
  double fy = 0.5,
  bool stayPut = false,
  String body = '',
}) async {
  await tapPhotoAt(tester, fx, fy);
  await tester.enterText(find.byKey(const ValueKey('chapter-title')), title);
  if (body.isNotEmpty) {
    await tester.enterText(find.byKey(const ValueKey('chapter-body')), body);
  }
  await tester.pump();
  if (stayPut) {
    await tester.ensureVisible(find.byKey(const ValueKey('stay-put')));
    await tester.tap(find.byKey(const ValueKey('stay-put')));
    await tester.pump();
  }
  await tester.ensureVisible(find.byKey(const ValueKey('add-chapter')));
  await tester.tap(find.byKey(const ValueKey('add-chapter')));
  await tester.pumpAndSettle();
}
