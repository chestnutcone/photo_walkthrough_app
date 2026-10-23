import 'package:flutter/widgets.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

/// Opens the inquiry sheet about [workId]. [type] pre-selects the inquiry type
/// (the story end card passes "Buy a print").
///
/// B7 overrides [openInquirySheetProvider] with the real flow.
typedef OpenInquirySheet = Future<void> Function(
  BuildContext context, {
  required String artistId,
  required String workId,
  String? type,
});

/// Default: no-op.
final openInquirySheetProvider = Provider<OpenInquirySheet>(
  (_) => (context, {required artistId, required workId, type}) async {},
);
