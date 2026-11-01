import 'dart:async';
import 'dart:convert';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_riverpod/misc.dart' show Override;
import 'package:flutter_test/flutter_test.dart';
import 'package:vela_app/core/api/api_providers.dart';
import 'package:vela_app/core/auth/session_store.dart';
import 'package:vela_app/core/router.dart';
import 'package:vela_app/core/theme/app_theme.dart';
import 'package:vela_app/features/create/blob_uploader.dart';
import 'package:vela_app/features/create/photo_info.dart';
import 'package:vela_app/features/create/photo_picker.dart';
import 'package:vela_app/features/onboarding/legal_links.dart';

import '../../../core/support/avatar_marker.dart';
import '../../../core/support/fakes.dart';
import '../../create/support/create_harness.dart'
    show FakeBlobUploader, FakePhotoPicker, tinyPng;

export '../../create/support/create_harness.dart' show tinyPng;

Finder key(String k) => find.byKey(ValueKey(k));

Map<String, Object?> myProfile({
  String handle = 'ana',
  String? publicEmail,
  String website = 'https://ana.example',
  List<Map<String, String>> services = const [],
  List<Map<String, String>> shows = const [],
}) => {
  'id': 'u1',
  'handle': handle,
  'displayName': 'Ana Ruiz',
  'bio': 'Light and salt.',
  'followerCount': 3,
  'followingCount': 1,
  'workCount': 2,
  'onboardingComplete': true,
  'city': 'Lisbon',
  'website': website,
  'publicEmail': ?publicEmail,
  'services': services,
  'shows': shows,
};

/// Scripted API for edit profile and settings.
class EditApi {
  EditApi({
    Map<String, Object?>? profile,
    this.patchStatus = 200,
    this.patchCode = 'validation_failed',
    this.patchMessage = 'bio must be at most 280 characters.',
    this.deleteStatus = 200,
    this.deleteCode = 'server_error',
    this.taken = const {},
    this.exportRequestStatus = 200,
    this.exportStatuses = const ['pending', 'ready'],
  }) : profile = profile ?? myProfile();

  Map<String, Object?> profile;
  int patchStatus;
  String patchCode;
  String patchMessage;
  int deleteStatus;
  String deleteCode;
  Set<String> taken;
  int exportRequestStatus;

  /// Statuses returned by successive `GET /v1/me/export/e1`.
  List<String> exportStatuses;
  int _exportGets = 0;
  Completer<void>? deleteGate;
  final List<RequestOptions> all = [];

  Iterable<RequestOptions> to(String method, String path) =>
      all.where((r) => r.method == method && r.path == path);

  Map<String, dynamic> body(RequestOptions r) => Map<String, dynamic>.from(
    (r.data is String ? jsonDecode(r.data as String) : r.data) as Map,
  );

  Map<String, Object?> get me => {
    'id': 'u1',
    'profile': profile,
    'onboardingComplete': true,
    'tier': {'name': 'free', 'maxWorks': 20},
    'usedWorks': 0,
  };

  Map<String, Object?> _export(String status) => {
    'id': 'e1',
    'status': status,
    'downloadUrl': status == 'ready'
        ? 'https://blob.test/export.zip?sig=SECRET'
        : null,
    'createdAt': '2026-10-23T10:00:00Z',
    'expiresAt': '2026-10-30T10:00:00Z',
  };

  Future<ResponseBody> handle(RequestOptions o) async {
    all.add(o);
    final path = o.path;
    if (path.startsWith('/v1/artists/') && path != '/v1/artists/suggested') {
      final h = path.substring('/v1/artists/'.length);
      return taken.contains(h)
          ? jsonResponse(200, okEnvelope(myProfile(handle: h)))
          : jsonResponse(404, errEnvelope('artist_not_found'));
    }
    switch ('${o.method} $path') {
      case 'GET /v1/me':
        return jsonResponse(200, okEnvelope(me));
      case 'PATCH /v1/me/profile':
        if (patchStatus != 200) {
          return jsonResponse(
            patchStatus,
            errEnvelope(patchCode, patchMessage),
          );
        }
        final b = body(o);
        profile = {...profile, ...b..remove('avatarUploaded')};
        return jsonResponse(200, okEnvelope(me));
      case 'POST /v1/me/avatar-upload':
        return jsonResponse(
          200,
          okEnvelope({
            'uploadUrl': 'https://blob.test/avatars/u1?sig=abc',
            'expiresAt': '2030-01-01T00:00:00Z',
          }),
        );
      case 'DELETE /v1/me':
        await deleteGate?.future;
        return deleteStatus == 200
            ? jsonResponse(200, okEnvelope(null))
            : jsonResponse(deleteStatus, errEnvelope(deleteCode));
      case 'POST /v1/auth/refresh':
        return jsonResponse(401, errEnvelope('unauthorized'));
      case 'POST /v1/auth/logout':
        return jsonResponse(200, okEnvelope(null));
      case 'POST /v1/me/export':
        return exportRequestStatus == 200
            ? jsonResponse(200, okEnvelope(_export('pending')))
            : jsonResponse(
                exportRequestStatus,
                errEnvelope(
                  'rate_limited',
                  'You can request one export per day.',
                ),
              );
      case 'GET /v1/me/export/e1':
        final n = _exportGets++;
        final i = n.clamp(0, exportStatuses.length - 1);
        return jsonResponse(200, okEnvelope(_export(exportStatuses[i])));
    }
    return jsonResponse(404, errEnvelope('not_found'));
  }
}

class EditHarness {
  EditHarness(
    this.api,
    this.storage,
    this.container,
    this.picker,
    this.uploader,
    this.launched,
  );
  final EditApi api;
  final FakeSessionStorage storage;
  final ProviderContainer container;
  final FakePhotoPicker picker;
  final FakeBlobUploader uploader;
  final List<Uri> launched;

  /// Top of the navigation stack (a pushed page does not change `uri`).
  String get location => container
      .read(routerProvider)
      .routerDelegate
      .currentConfiguration
      .last
      .matchedLocation;
}

final _session = Session(
  accessToken: 'A',
  refreshToken: 'R',
  accessExpiresAt: DateTime.utc(2030),
).encode();

/// Pumps the real app router for a signed-in account and opens [path].
Future<EditHarness> pumpEdit(
  WidgetTester tester, {
  required String path,
  EditApi? api,
  bool launchSucceeds = true,
  List<Override> overrides = const [],
}) async {
  tester.view.physicalSize = const Size(390, 1800);
  tester.view.devicePixelRatio = 1;
  addTearDown(tester.view.reset);
  final a = api ?? EditApi();
  final storage = FakeSessionStorage(_session);
  final picker = FakePhotoPicker(PickedPhoto(tinyPng));
  final uploader = FakeBlobUploader();
  final launched = <Uri>[];
  await tester.pumpWidget(
    ProviderScope(
      overrides: [
        sessionStorageProvider.overrideWithValue(storage),
        httpClientAdapterProvider.overrideWithValue(FakeAdapter(a.handle)),
        photoPickerProvider.overrideWithValue(picker),
        blobUploaderProvider.overrideWithValue(uploader),
        photoNormalizerProvider.overrideWithValue(markerNormalizer),
        legalUrlLauncherProvider.overrideWithValue((uri) async {
          launched.add(uri);
          return launchSucceeds;
        }),
        ...overrides,
      ],
      child: Consumer(
        builder: (context, ref, _) => MaterialApp.router(
          theme: buildTheme(),
          routerConfig: ref.watch(routerProvider),
        ),
      ),
    ),
  );
  final container = ProviderScope.containerOf(
    tester.element(find.byType(MaterialApp)),
  );
  await settle(tester);
  container.read(routerProvider).push(path);
  await settle(tester);
  return EditHarness(a, storage, container, picker, uploader, launched);
}

Future<void> settle(WidgetTester tester) async {
  for (var i = 0; i < 4; i++) {
    await tester.pump(const Duration(milliseconds: 60));
  }
  await tester.pumpAndSettle();
}

Future<void> typeInto(WidgetTester tester, String k, String text) async {
  await tester.ensureVisible(key(k));
  await tester.enterText(key(k), text);
  await tester.pump(const Duration(milliseconds: 600));
  await tester.pump();
}

Future<void> tapKey(WidgetTester tester, String k) async {
  await tester.ensureVisible(key(k));
  await tester.tap(key(k));
  await settle(tester);
}
