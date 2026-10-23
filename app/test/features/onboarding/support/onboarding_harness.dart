import 'dart:async';
import 'dart:convert';
import 'dart:typed_data';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_riverpod/misc.dart' show Override;
import 'package:flutter_test/flutter_test.dart';
import 'package:vela_app/core/api/api_providers.dart';
import 'package:vela_app/core/auth/session_store.dart';
import 'package:vela_app/core/router.dart';
import 'package:vela_app/core/theme/app_theme.dart';
import 'package:vela_app/features/onboarding/avatar_picker.dart';

import '../../../core/support/fakes.dart';

const phone = Size(390, 844);

final Uint8List png = base64Decode(
  'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==',
);

Map<String, Object?> profileJson(String id, String handle, String name) => {
  'id': id,
  'handle': handle,
  'displayName': name,
  'bio': '',
  'followerCount': 3,
  'followingCount': 1,
  'workCount': 2,
  'onboardingComplete': true,
  'discipline': 'Photographer',
};

List<Map<String, Object?>> suggestions(int n) => [
  for (var i = 1; i <= n; i++)
    profileJson('s$i', 'artist$i', 'Artist Number$i'),
];

/// Scripted API for the onboarding flow.
class OnboardingApi {
  OnboardingApi({
    this.taken = const {},
    int suggestedCount = 5,
    this.suggestedStatus = 200,
    this.onboardingStatus = 200,
    this.onboardingCode = 'handle_taken',
    this.uploadStatus = 201,
  }) : suggested = suggestions(suggestedCount);

  Set<String> taken;
  List<Map<String, Object?>> suggested;
  int suggestedStatus;
  int onboardingStatus;
  String onboardingCode;
  int uploadStatus;

  /// Handle lookups wait on these when present.
  final Map<String, Completer<void>> gates = {};

  bool onboarded = false;
  final List<RequestOptions> all = [];
  final List<String> order = [];

  Iterable<RequestOptions> to(String path, [String? method]) => all.where(
    (r) => r.path == path && (method == null || r.method == method),
  );

  Map<String, dynamic> body(RequestOptions r) => Map<String, dynamic>.from(
    (r.data is String ? jsonDecode(r.data as String) : r.data) as Map,
  );

  Future<ResponseBody> handle(RequestOptions o) async {
    all.add(o);
    final path = o.path;
    if (path.startsWith('/v1/artists/') && path != '/v1/artists/suggested') {
      final handle = path.substring('/v1/artists/'.length);
      await gates[handle]?.future;
      return taken.contains(handle)
          ? jsonResponse(200, okEnvelope(profileJson('x', handle, 'Someone')))
          : jsonResponse(404, errEnvelope('artist_not_found'));
    }
    switch (path) {
      case '/v1/artists/suggested':
        return suggestedStatus == 200
            ? jsonResponse(200, okEnvelope(suggested))
            : jsonResponse(suggestedStatus, errEnvelope('server_error'));
      case '/v1/me':
        return jsonResponse(
          200,
          okEnvelope({
            'id': 'u1',
            'profile': null,
            'onboardingComplete': onboarded,
            'tier': {'name': 'free', 'maxWorks': 20},
            'usedWorks': 0,
          }),
        );
      case '/v1/me/onboarding':
        order.add('onboarding');
        if (onboardingStatus != 200) {
          return jsonResponse(onboardingStatus, errEnvelope(onboardingCode));
        }
        onboarded = true;
        return jsonResponse(
          200,
          okEnvelope({
            'id': 'u1',
            'profile': profileJson('u1', 'ana', 'Ana'),
            'onboardingComplete': true,
            'tier': {'name': 'free', 'maxWorks': 20},
            'usedWorks': 0,
          }),
        );
      case '/v1/me/avatar-upload':
        order.add('ticket');
        return jsonResponse(
          200,
          okEnvelope({
            'uploadUrl': 'https://blob.test/avatars/u1?sig=abc',
            'expiresAt': '2030-01-01T00:00:00Z',
          }),
        );
      case '/v1/me/profile':
        order.add('profile');
        return jsonResponse(
          200,
          okEnvelope({
            'id': 'u1',
            'profile': profileJson('u1', 'ana', 'Ana'),
            'onboardingComplete': true,
            'tier': {'name': 'free', 'maxWorks': 20},
            'usedWorks': 0,
          }),
        );
    }
    if (o.uri.host == 'blob.test') {
      order.add('upload');
      return ResponseBody.fromString('', uploadStatus);
    }
    return jsonResponse(404, errEnvelope('not_found'));
  }
}

class FakeAvatarPicker implements AvatarPicker {
  FakeAvatarPicker({this.result});
  PickedAvatar? result;
  int picks = 0;

  @override
  Future<PickedAvatar?> pick() async {
    picks++;
    return result;
  }
}

class OnboardingHarness {
  OnboardingHarness(this.api, this.storage, this.container);
  final OnboardingApi api;
  final FakeSessionStorage storage;
  final ProviderContainer container;
}

final _session = Session(
  accessToken: 'A',
  refreshToken: 'R',
  accessExpiresAt: DateTime.utc(2030),
).encode();

/// The real router with a signed-in, not yet onboarded account.
Future<OnboardingHarness> pumpOnboarding(
  WidgetTester tester, {
  OnboardingApi? api,
  AvatarPicker? picker,
  List<Override> overrides = const [],
  bool onboarded = false,
}) async {
  tester.view.physicalSize = phone;
  tester.view.devicePixelRatio = 1;
  addTearDown(tester.view.reset);
  final a = api ?? OnboardingApi();
  a.onboarded = onboarded;
  final storage = FakeSessionStorage(_session);
  await tester.pumpWidget(
    ProviderScope(
      overrides: [
        sessionStorageProvider.overrideWithValue(storage),
        httpClientAdapterProvider.overrideWithValue(FakeAdapter(a.handle)),
        if (picker != null) avatarPickerProvider.overrideWithValue(picker),
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
  // /v1/me answers after the first frame; the redirect follows it.
  await settle(tester);
  return OnboardingHarness(a, storage, container);
}

/// Router changes land one frame after the animations settle.
Future<void> settle(WidgetTester tester) async {
  for (var i = 0; i < 4; i++) {
    await tester.pump(const Duration(milliseconds: 60));
  }
  await tester.pumpAndSettle();
  // The router resolves redirects in microtasks that run between pumps.
  for (var i = 0; i < 3; i++) {
    await tester.pump(const Duration(milliseconds: 20));
  }
  await tester.pumpAndSettle();
}

Finder key(String k) => find.byKey(ValueKey(k));

/// Types a handle and lets the debounce and the lookup finish.
Future<void> typeHandle(WidgetTester tester, String handle) async {
  await tester.enterText(key('handle-field'), handle);
  await tester.pump(const Duration(milliseconds: 600));
  await settle(tester);
}

/// Welcome -> profile with a valid, available handle and both checkboxes.
Future<void> fillProfile(
  WidgetTester tester, {
  String name = 'Ana Ruiz',
  String handle = 'ana',
  bool age = true,
  bool terms = true,
}) async {
  await tester.tap(key('welcome-continue'));
  await settle(tester);
  await tester.enterText(key('name-field'), name);
  await typeHandle(tester, handle);
  if (age) {
    await tester.ensureVisible(key('age-check'));
    await tester.tap(key('age-check'));
    await tester.pump();
  }
  if (terms) {
    await tester.ensureVisible(key('terms-check'));
    await tester.tap(key('terms-check'));
    await tester.pump();
  }
}
