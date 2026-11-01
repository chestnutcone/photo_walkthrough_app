import 'package:flutter/widgets.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/require_sign_in.dart';
import 'report_sheet.dart';

/// Opens the report flow for one target. [targetType] is one of the API's
/// `work`, `reply` or `user`. Completes true when a report was filed, so the
/// caller can hide the target locally.
typedef ShowReportSheet = Future<bool> Function(
  BuildContext context, {
  required String targetType,
  required String targetId,
});

/// The real flow: guests are asked to sign in first (cancelling sends
/// nothing), then the report sheet opens. Tests override it.
final reportSheetHook = Provider<ShowReportSheet>(
  (ref) => (context, {required targetType, required targetId}) async {
    final signedIn = await requireSignInWith(
      context,
      session: () => ref.read(sessionProvider.future),
      launcher: () => ref.read(signInSheetLauncherProvider),
    );
    if (!signedIn || !context.mounted) return false;
    return showReportSheet(context, targetType: targetType, targetId: targetId);
  },
);
