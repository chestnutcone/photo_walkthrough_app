import 'dart:async';

import 'package:flutter/widgets.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

/// Opens the replies sheet for one chapter. The returned future completes
/// when the sheet is dismissed; the story pauses autoplay until then.
/// [readOnly] (web viewer) means the sheet must not show a composer.
///
/// B6 overrides [openRepliesSheetProvider] with the real sheet.
typedef OpenRepliesSheet = Future<void> Function(
  BuildContext context,
  String workId,
  int chapterIndex, {
  required bool readOnly,
});

/// Default stub: shows nothing and never completes, so the story stays in its
/// "replies open" state (autoplay paused) until the pill is tapped again.
final openRepliesSheetProvider = Provider<OpenRepliesSheet>(
  (_) =>
      (context, workId, chapterIndex, {required readOnly}) =>
          Completer<void>().future,
);
