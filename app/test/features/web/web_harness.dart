import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:vela_app/core/api/api_providers.dart';
import 'package:vela_app/core/auth/auth_controller.dart';
import 'package:vela_app/core/auth/session_store.dart';
import 'package:vela_app/features/onboarding/legal_links.dart';
import 'package:vela_app/features/story/story_settings.dart';
import 'package:vela_app/features/web/web_viewer_app.dart';
import 'package:vela_app/features/web/web_viewer_config.dart';

import '../../core/support/fakes.dart';
import '../story/support/story_harness.dart'
    show phone, transparentPng, workJson;

Map<String, Object?> replyJson(String id, String body) => {
  'id': id,
  'workId': 'w1',
  'chapterIndex': 0,
  'author': {
    'id': 'p1',
    'handle': 'sam',
    'displayName': 'Sam',
    'bio': '',
    'followerCount': 0,
    'followingCount': 0,
    'workCount': 1,
    'onboardingComplete': true,
  },
  'body': body,
  'createdAt': '2026-10-03T10:00:00Z',
};

/// A signed-in session already in memory. The web viewer must still send no
/// credentials, so only its Dio (without the bearer interceptor) protects.
class _SeededSession extends SessionNotifier {
  @override
  Future<Session?> build() async => Session(
    accessToken: 'SECRET',
    refreshToken: 'R',
    accessExpiresAt: DateTime.utc(2030),
  );
}

class WebHarness {
  WebHarness(this.adapter, this.launched);
  final FakeAdapter adapter;
  final List<Uri> launched;
}

/// Pumps the web viewer exactly as `main_web.dart` builds it, with a fake
/// transport.
Future<WebHarness> pumpWeb(
  WidgetTester tester, {
  String location = '/s/w1',
  String? platformRoute,
  int workStatus = 200,
  String errorCode = 'not_found',
  WebViewerConfig config = const WebViewerConfig(),
  List<Map<String, Object?>>? replies,
  bool storedSession = false,
}) async {
  tester.view.physicalSize = phone;
  tester.view.devicePixelRatio = 1;
  addTearDown(tester.view.reset);
  // What the engine reports as the initial route: the browser path under the
  // path URL strategy.
  if (platformRoute != null) {
    tester.binding.platformDispatcher.defaultRouteNameTestValue = platformRoute;
    addTearDown(
      tester.binding.platformDispatcher.clearDefaultRouteNameTestValue,
    );
  }
  final adapter = FakeAdapter((o) {
    if (o.path == '/v1/works/w1') {
      return workStatus == 200
          ? jsonResponse(200, okEnvelope(workJson()))
          : jsonResponse(workStatus, errEnvelope(errorCode));
    }
    if (o.path == '/v1/works/w1/chapters/0/replies') {
      return jsonResponse(200, {
        'success': true,
        'data': replies ?? [replyJson('r1', 'Lovely light')],
        'error': null,
        'page': {'nextCursor': null},
      });
    }
    return jsonResponse(404, errEnvelope('not_found'));
  });
  final launched = <Uri>[];
  await tester.pumpWidget(
    ProviderScope(
      overrides: [
        if (storedSession) sessionProvider.overrideWith(_SeededSession.new),
        ...webViewerOverrides(),
        httpClientAdapterProvider.overrideWithValue(adapter),
        webViewerConfigProvider.overrideWithValue(config),
        storyAutoplayDefaultProvider.overrideWithValue(false),
        storyImageProviderFactoryProvider.overrideWithValue(
          (_) => MemoryImage(transparentPng),
        ),
        legalUrlLauncherProvider.overrideWithValue((uri) async {
          launched.add(uri);
          return true;
        }),
      ],
      child: platformRoute != null
          ? const WebViewerApp()
          : WebViewerApp(initialLocation: location),
    ),
  );
  await tester.pumpAndSettle();
  return WebHarness(adapter, launched);
}

String pretty(Object o) => const JsonEncoder.withIndent(' ').convert(o);
