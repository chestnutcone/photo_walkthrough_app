import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:url_launcher/url_launcher.dart';

import '../../core/config.dart';

enum LegalDoc { terms, privacy }

/// The public legal pages are served by the API at `/legal/{slug}`.
Uri legalUri(LegalDoc doc) =>
    Uri.parse('${AppConfig.apiBaseUrl}/legal/${doc.name}');

/// https always; plain http only for a local development server in a debug
/// build (localhost, loopback, or the Android emulator's host alias).
bool isLaunchableLegalUri(Uri uri, {bool isDebug = kDebugMode}) {
  if (uri.host.isEmpty) return false;
  if (uri.scheme == 'https') return true;
  const local = {'localhost', '127.0.0.1', '10.0.2.2'};
  return isDebug && uri.scheme == 'http' && local.contains(uri.host);
}

/// Opens [uri] in the external browser; false when it cannot be opened.
typedef LegalUrlLauncher = Future<bool> Function(Uri uri);

final legalUrlLauncherProvider = Provider<LegalUrlLauncher>(
  (_) => (uri) async {
    try {
      return await launchUrl(uri, mode: LaunchMode.externalApplication);
    } on Object {
      return false;
    }
  },
);

typedef LegalLinkOpener = Future<void> Function(BuildContext, Uri);

/// Test seam for the Terms and Privacy links.
final legalLinkOpenerProvider = Provider<LegalLinkOpener>((ref) {
  final launch = ref.watch(legalUrlLauncherProvider);
  return (context, uri) async {
    final messenger = ScaffoldMessenger.of(context);
    final opened = isLaunchableLegalUri(uri) && await launch(uri);
    if (!opened) {
      messenger.showSnackBar(
        const SnackBar(
          content: Text("Couldn't open the page. Try again later."),
        ),
      );
    }
  };
});
