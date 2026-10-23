import 'dart:async';

import 'package:flutter/widgets.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../replies/replies_sheet.dart';
import 'story_data.dart';

/// Opens the replies sheet for one chapter. The returned future completes
/// when the sheet is dismissed; the story pauses autoplay until then.
/// [readOnly] (web viewer) means the sheet must not show a composer.
typedef OpenRepliesSheet = Future<void> Function(
  BuildContext context,
  String workId,
  int chapterIndex, {
  required bool readOnly,
});

/// Real sheet by default; tests override it. The chapter title comes from the
/// already-loaded story.
final openRepliesSheetProvider = Provider<OpenRepliesSheet>(
  (ref) => (context, workId, chapterIndex, {required readOnly}) {
    final chapters = ref.read(workProvider(workId)).value?.chapters;
    final title =
        chapters != null && chapterIndex >= 0 && chapterIndex < chapters.length
        ? chapters[chapterIndex].title
        : '';
    return showRepliesSheet(
      context,
      workId: workId,
      chapterIndex: chapterIndex,
      chapterTitle: title,
      readOnly: readOnly,
    );
  },
);
