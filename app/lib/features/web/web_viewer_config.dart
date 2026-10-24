import 'package:flutter/foundation.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/branding.dart';
import '../../core/config.dart';

/// Build-time settings of the web story viewer.
@immutable
class WebViewerConfig {
  const WebViewerConfig({
    this.appStoreUrl = '',
    this.playStoreUrl = '',
    this.linksDomain = Branding.domain,
  });

  final String appStoreUrl;
  final String playStoreUrl;

  /// Host of the universal / app links.
  final String linksDomain;

  /// Only https store links are used; anything else hides the button.
  Uri? get appStoreUri => _https(appStoreUrl);
  Uri? get playStoreUri => _https(playStoreUrl);

  /// The custom-scheme link that opens the story in the installed app.
  Uri deepLinkFor(String workId) => Uri(
    scheme: Branding.urlScheme,
    host: 'open',
    pathSegments: ['s', workId],
  );

  /// What "Open in app" opens. On a phone the https link is preferred: with
  /// the app installed the OS hands it to the app, otherwise the browser
  /// shows the share page. The custom scheme is the fallback elsewhere.
  Uri openInAppUri(String workId, {required bool mobile}) => mobile
      ? Uri(scheme: 'https', host: linksDomain, pathSegments: ['s', workId])
      : deepLinkFor(workId);

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
    linksDomain: AppConfig.linksDomain,
  ),
);
