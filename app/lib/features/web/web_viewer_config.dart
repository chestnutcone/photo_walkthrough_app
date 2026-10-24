import 'package:flutter/foundation.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/branding.dart';
import '../../core/config.dart';

/// Build-time settings of the web story viewer.
@immutable
class WebViewerConfig {
  const WebViewerConfig({this.appStoreUrl = '', this.playStoreUrl = ''});

  final String appStoreUrl;
  final String playStoreUrl;

  /// Only https store links are used; anything else hides the button.
  Uri? get appStoreUri => _https(appStoreUrl);
  Uri? get playStoreUri => _https(playStoreUrl);

  /// The custom-scheme link that opens the story in the installed app.
  Uri deepLinkFor(String workId) => Uri(
    scheme: Branding.urlScheme,
    host: 'open',
    pathSegments: ['s', workId],
  );

  static Uri? _https(String raw) {
    final uri = Uri.tryParse(raw.trim());
    return uri != null && uri.scheme == 'https' && uri.host.isNotEmpty
        ? uri
        : null;
  }
}

final webViewerConfigProvider = Provider<WebViewerConfig>(
  (_) => const WebViewerConfig(
    appStoreUrl: AppConfig.appStoreUrl,
    playStoreUrl: AppConfig.playStoreUrl,
  ),
);
