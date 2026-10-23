import 'package:flutter/widgets.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:vela_api_client/vela_api_client.dart';

import '../../core/api/api_providers.dart';
import '../../core/require_sign_in.dart';
import '../portfolio/contact_sheet.dart';
import 'story_data.dart';

/// Opens the inquiry sheet about [workId]. [type] pre-selects the inquiry type
/// (the story end card passes "Buy a print").
typedef OpenInquirySheet = Future<void> Function(
  BuildContext context, {
  required String artistId,
  required String workId,
  String? type,
});

/// Default: the real contact sheet. The inquiry endpoint needs an account, so
/// guests are asked to sign in first. The artist's name and the work's title
/// and thumbnail come from the story that is already loaded.
final openInquirySheetProvider = Provider<OpenInquirySheet>(
  (ref) => (context, {required artistId, required workId, type}) async {
    final session = await ref.read(sessionProvider.future);
    if (session == null) {
      if (!context.mounted) return;
      if (!await ref.read(signInSheetLauncherProvider)(context)) return;
    }
    Work? work;
    try {
      work = await ref.read(workProvider(workId).future);
    } on Object {
      // The sheet still works without the title and thumbnail.
      work = null;
    }
    if (!context.mounted) return;
    await showContactSheet(
      context,
      InquiryTarget(
        artistId: artistId,
        artistName: work?.author.displayName ?? '',
        workId: workId,
        workTitle: work?.title,
        workThumbUrl: work?.thumbUrl ?? work?.feedUrl,
        initialType: type,
      ),
    );
  },
);
