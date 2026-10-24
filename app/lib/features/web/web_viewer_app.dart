import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/misc.dart' show Override;
import 'package:go_router/go_router.dart';

import '../../core/api/api_providers.dart';
import '../../core/auth/session_store.dart';
import '../../core/branding.dart';
import '../../core/theme/app_theme.dart';
import '../../core/theme/tokens.dart';
import '../story/story_screen.dart';
import 'open_in_app_banner.dart';

/// A session store that holds nothing and never touches secure storage.
class NoSessionStorage implements SessionStorage {
  const NoSessionStorage();

  @override
  Future<String?> read() async => null;
  @override
  Future<void> write(String json) async {}
  @override
  Future<void> delete() async {}
}

/// Provider overrides that make the web viewer anonymous: no stored session
/// and an API client without the bearer interceptor, so no request can carry
/// credentials.
List<Override> webViewerOverrides() => [
  sessionStorageProvider.overrideWithValue(const NoSessionStorage()),
  dioProvider.overrideWith((ref) => ref.watch(refreshDioProvider)),
];

/// The only route is `/s/:workId`; everything else is "not available".
GoRouter buildWebRouter({String initialLocation = '/'}) => GoRouter(
  initialLocation: initialLocation,
  errorBuilder: (_, _) => const WebUnavailable(),
  routes: [
    GoRoute(
      path: '/s/:workId',
      builder: (_, s) => WebStoryPage(workId: s.pathParameters['workId']!),
    ),
  ],
);

class WebViewerApp extends StatefulWidget {
  const WebViewerApp({super.key, this.initialLocation = '/'});

  final String initialLocation;

  @override
  State<WebViewerApp> createState() => _WebViewerAppState();
}

class _WebViewerAppState extends State<WebViewerApp> {
  late final GoRouter _router = buildWebRouter(
    initialLocation: widget.initialLocation,
  );

  @override
  void dispose() {
    _router.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => MaterialApp.router(
    title: Branding.appName,
    theme: buildTheme(),
    routerConfig: _router,
  );
}

/// Read-only story with the "Open in app" strip on top.
class WebStoryPage extends StatelessWidget {
  const WebStoryPage({super.key, required this.workId});

  final String workId;

  @override
  Widget build(BuildContext context) => Stack(
    children: [
      Positioned.fill(
        child: StoryScreen(workId: workId, readOnly: true, standalone: true),
      ),
      Positioned(
        left: 0,
        right: 0,
        top: 0,
        child: OpenInAppBanner(workId: workId),
      ),
    ],
  );
}

/// Same wording as the story viewer's own unavailable state, so a bad path
/// and a hidden story look alike.
class WebUnavailable extends StatelessWidget {
  const WebUnavailable({super.key});

  @override
  Widget build(BuildContext context) => Scaffold(
    backgroundColor: Colors.black,
    body: Center(
      child: Text("This story isn't available", style: VelaText.serif24),
    ),
  );
}
