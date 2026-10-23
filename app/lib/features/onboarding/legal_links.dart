import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/config.dart';

enum LegalDoc { terms, privacy }

/// The public legal pages are served by the API at `/legal/{slug}`.
Uri legalUri(LegalDoc doc) =>
    Uri.parse('${AppConfig.apiBaseUrl}/legal/${doc.name}');

typedef LegalLinkOpener = Future<void> Function(BuildContext, Uri);

/// There is no `url_launcher` dependency yet, so the link is copied for the
/// user to open in a browser. Swap this provider for a launcher to open it
/// directly; no other code changes.
final legalLinkOpenerProvider = Provider<LegalLinkOpener>(
  (_) => (context, uri) async {
    await Clipboard.setData(ClipboardData(text: uri.toString()));
    if (!context.mounted) return;
    ScaffoldMessenger.of(context).showSnackBar(
      const SnackBar(
        content: Text('Link copied. Paste it into a browser to read it.'),
      ),
    );
  },
);
