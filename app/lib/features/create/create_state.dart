import 'package:flutter/foundation.dart';
import 'package:vela_api_client/vela_api_client.dart';

import 'pin_draft.dart';

enum CreatePhase { pick, annotate, publishing }

/// How publishing ended up, once it is no longer in flight.
enum PublishOutcome { processing, published, blocked, removed, stillProcessing }

/// The photo as the author sees it: bytes already in display orientation.
@immutable
class DraftPhoto {
  const DraftPhoto({
    required this.bytes,
    required this.width,
    required this.height,
    required this.contentType,
  });

  final Uint8List bytes;
  final int width;
  final int height;
  final String contentType;

  double get aspect => width / height;
}

const _keep = Object();

@immutable
class CreateState {
  const CreateState({
    this.phase = CreatePhase.pick,
    this.workId,
    this.title = '',
    this.altText = '',
    this.pins = const [],
    this.ghost,
    this.editingIndex,
    this.photo,
    this.busy = false,
    this.error,
    this.uploadFailed = false,
    this.outcome,
    this.aborted = false,
  });

  final CreatePhase phase;
  final String? workId;
  final String title;
  final String altText;
  final List<PinDraft> pins;
  final PinDraft? ghost;

  /// Index of the chapter being edited, or null when [ghost] is a new one.
  final int? editingIndex;
  final DraftPhoto? photo;

  /// Creating the draft / uploading / saving.
  final bool busy;

  /// A message for the author, or null.
  final String? error;

  /// The draft exists but its bytes did not reach storage.
  final bool uploadFailed;
  final PublishOutcome? outcome;

  /// The session ended mid-flow; the screen leaves.
  final bool aborted;

  bool get hasUnsavedWork => pins.isNotEmpty || ghost != null;
  bool get canPublish =>
      pins.isNotEmpty &&
      !busy &&
      phase == CreatePhase.annotate &&
      !uploadFailed;

  CreateState copyWith({
    CreatePhase? phase,
    Object? workId = _keep,
    String? title,
    String? altText,
    List<PinDraft>? pins,
    Object? ghost = _keep,
    Object? editingIndex = _keep,
    Object? photo = _keep,
    bool? busy,
    Object? error = _keep,
    bool? uploadFailed,
    Object? outcome = _keep,
    bool? aborted,
  }) => CreateState(
    phase: phase ?? this.phase,
    workId: identical(workId, _keep) ? this.workId : workId as String?,
    title: title ?? this.title,
    altText: altText ?? this.altText,
    pins: pins ?? this.pins,
    ghost: identical(ghost, _keep) ? this.ghost : ghost as PinDraft?,
    editingIndex: identical(editingIndex, _keep)
        ? this.editingIndex
        : editingIndex as int?,
    photo: identical(photo, _keep) ? this.photo : photo as DraftPhoto?,
    busy: busy ?? this.busy,
    error: identical(error, _keep) ? this.error : error as String?,
    uploadFailed: uploadFailed ?? this.uploadFailed,
    outcome: identical(outcome, _keep)
        ? this.outcome
        : outcome as PublishOutcome?,
    aborted: aborted ?? this.aborted,
  );
}

/// Chapters for the update call, in the order they were added.
UpdateWorkRequest buildUpdate(CreateState s) => UpdateWorkRequest(
  title: s.title.trim().isEmpty ? defaultStoryTitle : s.title.trim(),
  altText: s.altText.trim().isEmpty ? null : s.altText.trim(),
  chapters: [for (final p in s.pins) p.toChapter()],
);

/// README: a story published without a title is "Untitled study".
const defaultStoryTitle = 'Untitled study';
