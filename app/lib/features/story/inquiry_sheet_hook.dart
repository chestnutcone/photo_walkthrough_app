import 'package:flutter/widgets.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

/// Opens the inquiry sheet pre-filled for "Buy a print" on [workId].
///
/// B7 overrides [openInquirySheetProvider] with the real flow.
typedef OpenInquirySheet = Future<void> Function(
  BuildContext context, {
  required String artistId,
  required String workId,
});

/// Default: no-op.
final openInquirySheetProvider = Provider<OpenInquirySheet>(
  (_) => (context, {required artistId, required workId}) async {},
);
