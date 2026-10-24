import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import 'features/web/web_viewer_app.dart';

/// Entry point of the public web story viewer:
/// `flutter build web -t lib/main_web.dart`. No session, no sign-in.
void main() => runApp(
  ProviderScope(overrides: webViewerOverrides(), child: const WebViewerApp()),
);
