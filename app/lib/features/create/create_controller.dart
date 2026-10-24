import 'dart:async';
import 'dart:math' as math;
import 'dart:ui' show Offset;

import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:vela_api_client/vela_api_client.dart';

import '../../core/api/api_exception.dart';
import '../../core/api/api_providers.dart';
import '../portfolio/portfolio_data.dart' show meProvider;
import 'blob_uploader.dart';
import 'create_messages.dart';
import 'create_state.dart';
import 'photo_info.dart';
import 'photo_picker.dart';
import 'pin_draft.dart';

/// Largest original accepted on the client. The server enforces no byte
/// limit of its own on the blob, so this keeps uploads sane on mobile data.
const int maxUploadBytes = 20 * 1024 * 1024;

/// Polling: 1s, 2s, 3s, 4s then 5s steps, giving up after two minutes.
const Duration pollTimeout = Duration(minutes: 2);
const int _maxPollStepSeconds = 5;

/// Waiting before the one background retry of a draft delete.
final backgroundRetryDelayProvider = Provider<Future<void> Function(Duration)>(
  (_) =>
      (d) => Future<void>.delayed(d),
);

/// Test seam for waiting between polls.
final pollDelayProvider = Provider<Future<void> Function(Duration)>(
  (_) =>
      (d) => Future<void>.delayed(d),
);

final createControllerProvider =
    NotifierProvider.autoDispose<CreateController, CreateState>(
      CreateController.new,
    );

class CreateController extends Notifier<CreateState> {
  /// Bumped to cancel anything in flight (leave, session change, new pick).
  int _gen = 0;
  late int _epoch;
  Uri? _uploadUrl;
  bool _uploaded = false;

  /// Set once publishWork has been sent: the story may no longer be a draft,
  /// so it must never be deleted without checking its status first.
  bool _publishRequested = false;

  WorksApi get _api => ref.read(apiProvider).getWorksApi();

  @override
  CreateState build() {
    _epoch = ref.read(sessionEpochProvider);
    ref.listen(sessionEpochProvider, (_, e) {
      if (e != _epoch) _abort();
    });
    ref.listen(sessionProvider, (_, s) {
      if (s.hasValue && s.value == null) _abort();
    });
    ref.onDispose(() => _gen++);
    return const CreateState();
  }

  bool _stale(int gen) => gen != _gen || !ref.mounted;

  void _abort() {
    _gen++;
    state = state.copyWith(aborted: true, busy: false);
  }

  /// The request `updateWork` receives; public so tests can inspect it.
  UpdateWorkRequest buildUpdateRequest() => buildUpdate(state);

  // ---- photo ----

  Future<void> pickPhoto(PhotoSource source) async {
    if (state.busy || state.phase != CreatePhase.pick) return;
    final gen = ++_gen;
    state = state.copyWith(busy: true, error: null);
    try {
      final picked = await ref.read(photoPickerProvider).pick(source);
      if (_stale(gen)) return;
      if (picked == null) {
        state = state.copyWith(busy: false);
        return;
      }
      final photo = await _prepare(picked, gen);
      if (photo == null) return;
      state = state.copyWith(photo: photo);
      await _ensureUploaded(gen, enterAnnotate: true);
    } on PhotoAccessDenied {
      if (_stale(gen)) return;
      state = state.copyWith(busy: false, error: photoAccessDeniedMessage);
    } on Object {
      if (_stale(gen)) return;
      state = state.copyWith(
        busy: false,
        error: createErrorMessage(
          const ApiException(code: 'unknown_error', message: ''),
        ),
      );
    }
  }

  /// Validates type and size and bakes in orientation. Null (with the error
  /// set) when the photo cannot be used.
  Future<DraftPhoto?> _prepare(PickedPhoto picked, int gen) async {
    var bytes = picked.bytes;
    var meta = readPhotoMeta(bytes);
    if (meta == null || !supportedTypes.contains(meta.contentType)) {
      state = state.copyWith(busy: false, error: unsupportedTypeMessage);
      return null;
    }
    if (meta.width * meta.height > maxPhotoPixels) {
      state = state.copyWith(busy: false, error: tooManyPixelsMessage);
      return null;
    }
    // Always: bakes in orientation and strips EXIF (GPS included).
    bytes = await ref.read(photoNormalizerProvider)(bytes);
    if (_stale(gen)) return null;
    meta = readPhotoMeta(bytes) ?? meta;
    if (bytes.length > maxUploadBytes) {
      state = state.copyWith(busy: false, error: tooLargeMessage);
      return null;
    }
    return DraftPhoto(
      bytes: bytes,
      width: meta.displayedWidth,
      height: meta.displayedHeight,
      contentType: meta.contentType,
    );
  }

  /// Creates the draft if there is none, then uploads the bytes. Returns
  /// whether the photo is safely on the server.
  Future<bool> _ensureUploaded(int gen, {bool enterAnnotate = false}) async {
    final photo = state.photo!;
    final api = _api;
    final delay = ref.read(backgroundRetryDelayProvider);
    state = state.copyWith(busy: true, error: null);
    try {
      if (state.workId == null) {
        final res = await apiGuard(
          () => _api.createDraft(
            createDraftRequest: CreateDraftRequest(
              contentType: _contentTypeEnum(photo.contentType),
            ),
          ),
        );
        final made = unwrapEnvelope(
          success: res.data?.success ?? false,
          data: res.data?.data,
          error: res.data?.error,
        );
        if (_stale(gen)) {
          // The author left while the draft was being made: free its slot.
          unawaited(_deleteDraft(api, delay, made.workId, verify: false));
          return false;
        }
        _uploadUrl = Uri.parse(made.uploadUrl);
        _uploaded = false;
        state = state.copyWith(
          workId: made.workId,
          phase: enterAnnotate ? CreatePhase.annotate : state.phase,
        );
      }
      if (!_uploaded) await _upload(photo, gen);
      if (_stale(gen)) return false;
      state = state.copyWith(busy: false, uploadFailed: false);
      return true;
    } on ApiException catch (e) {
      if (_stale(gen)) return false;
      await _onCreateFailed(e, gen);
      return false;
    } on BlobUploadException catch (e) {
      if (_stale(gen)) return false;
      if (e.expired) _dropDraft();
      state = state.copyWith(
        busy: false,
        uploadFailed: true,
        error: "Couldn't upload the photo. Check your connection and retry.",
      );
      return false;
    }
  }

  Future<void> _upload(DraftPhoto photo, int gen) async {
    await ref
        .read(blobUploaderProvider)
        .upload(
          url: _uploadUrl!,
          bytes: photo.bytes,
          contentType: photo.contentType,
        );
    if (_stale(gen)) return;
    _uploaded = true;
  }

  /// The signed URL stopped working: the draft is useless, free its slot.
  void _dropDraft() {
    final id = state.workId;
    state = state.copyWith(workId: null);
    _uploaded = false;
    if (id != null) {
      unawaited(
        _deleteDraft(
          _api,
          ref.read(backgroundRetryDelayProvider),
          id,
          verify: false,
        ),
      );
    }
  }

  Future<void> _onCreateFailed(ApiException e, int gen) async {
    if (e.code != 'quota_exceeded') {
      state = state.copyWith(busy: false, error: createErrorMessage(e));
      return;
    }
    int? used;
    int? max = quotaLimitFromMessage(e.message);
    try {
      ref.invalidate(meProvider);
      final me = await ref.read(meProvider.future);
      used = me?.usedWorks;
      max = me?.tier.maxWorks ?? max;
    } on Object {
      // The numbers fall back to the limit the server quoted.
    }
    if (_stale(gen)) return;
    final message = max == null
        ? 'You have reached your story limit. Delete one to add more.'
        : quotaMessage(used: math.max(used ?? max, max), max: max);
    state = state.copyWith(
      busy: false,
      error: message,
      photo: state.phase == CreatePhase.pick ? null : state.photo,
    );
  }

  Future<void> retryUpload() async {
    if (state.busy || state.photo == null) return;
    await _ensureUploaded(++_gen);
  }

  // ---- title ----

  void setTitle(String v) => state = state.copyWith(title: v);
  void setAltText(String v) => state = state.copyWith(altText: v);

  // ---- chapters ----

  /// Puts (or moves) the ghost pin at [fraction] of the displayed photo.
  void placeGhost(Offset fraction) {
    if (state.phase != CreatePhase.annotate) return;
    if (state.ghost == null && state.pins.length >= maxPins) {
      state = state.copyWith(error: maxPinsMessage);
      return;
    }
    final x = fraction.dx.clamp(0.0, 1.0);
    final y = fraction.dy.clamp(0.0, 1.0);
    final g = state.ghost;
    state = state.copyWith(
      error: null,
      ghost: g == null
          ? PinDraft(x: x, y: y)
          : PinDraft(
              x: x,
              y: y,
              zoom: g.zoom,
              title: g.title,
              body: g.body,
              meta: g.meta,
              altText: g.altText,
            ),
    );
  }

  /// Turns the ghost into a chapter (or replaces the one being edited).
  /// False when the title is empty or the story is full.
  bool commitGhost({
    required String title,
    required String body,
    required bool zoomIn,
    String? meta,
    String? altText,
  }) {
    final g = state.ghost;
    if (g == null || title.trim().isEmpty) return false;
    final pin = g.filled(
      zoom: zoomIn ? defaultZoom : null,
      title: title,
      body: body,
      meta: meta,
      altText: altText,
    );
    final at = state.editingIndex;
    if (at == null && state.pins.length >= maxPins) {
      state = state.copyWith(error: maxPinsMessage);
      return false;
    }
    final pins = [...state.pins];
    if (at == null) {
      pins.add(pin);
    } else {
      pins[at] = pin;
    }
    state = state.copyWith(
      pins: pins,
      ghost: null,
      editingIndex: null,
      error: null,
    );
    return true;
  }

  void discardGhost() =>
      state = state.copyWith(ghost: null, editingIndex: null, error: null);

  void editPin(int index) {
    if (index < 0 || index >= state.pins.length) return;
    state = state.copyWith(
      ghost: state.pins[index],
      editingIndex: index,
      error: null,
    );
  }

  void removePin(int index) {
    if (index < 0 || index >= state.pins.length) return;
    final pins = [...state.pins]..removeAt(index);
    state = state.copyWith(
      pins: pins,
      ghost: null,
      editingIndex: null,
      error: null,
    );
  }

  // ---- publish ----

  Future<void> publish() async {
    if (!state.canPublish) return;
    final gen = ++_gen;
    state = state.copyWith(busy: true, error: null, ghost: null);
    if (!await _ensureUploaded(gen)) return;
    try {
      final work = await _save(gen);
      if (work == null) return;
      state = state.copyWith(phase: CreatePhase.publishing, outcome: null);
      await _poll(work.id, gen);
    } on ApiException catch (e) {
      if (_stale(gen)) return;
      _publishFailed(e);
    }
  }

  /// updateWork then publishWork. Null when it failed (state says why).
  /// A 409 `not_editable` from either call means an earlier publish got
  /// through (its response was lost): carry on with the story as it is.
  Future<Work?> _save(int gen) async {
    final id = state.workId!;
    try {
      await apiGuard(
        () => _api.updateWork(id: id, updateWorkRequest: buildUpdateRequest()),
      );
      if (_stale(gen)) return null;
      _publishRequested = true;
      final res = await apiGuard(() => _api.publishWork(id: id));
      if (_stale(gen)) return null;
      final work = unwrapEnvelope(
        success: res.data?.success ?? false,
        data: res.data?.data,
        error: res.data?.error,
      );
      return work;
    } on ApiException catch (e) {
      if (e.code != 'not_editable') rethrow;
      final work = await _fetch(id);
      if (_stale(gen)) return null;
      if (work.status == WorkStatusEnum.draft) rethrow;
      _publishRequested = true;
      return work;
    }
  }

  void _publishFailed(ApiException e) {
    final gone = e.code == 'draft_expired' || e.code == 'work_not_found';
    if (gone) {
      _uploaded = false;
      state = state.copyWith(workId: null);
    }
    state = state.copyWith(
      busy: false,
      phase: CreatePhase.annotate,
      error: gone
          ? 'This draft expired. Your chapters are kept; publishing again '
                'starts a new draft.'
          : createErrorMessage(e),
    );
  }

  Future<Work> _fetch(String id) async {
    final res = await apiGuard(() => _api.getWork(id: id));
    return unwrapEnvelope(
      success: res.data?.success ?? false,
      data: res.data?.data,
      error: res.data?.error,
    );
  }

  Future<void> _poll(String id, int gen) async {
    var waited = Duration.zero;
    var step = 1;
    while (true) {
      WorkStatusEnum? status;
      try {
        status = (await _fetch(id)).status;
      } on ApiException catch (e) {
        if (_stale(gen)) return;
        final transient =
            e.statusCode == null || e.statusCode! >= 500 || e.statusCode == 429;
        if (!transient) {
          state = state.copyWith(
            busy: false,
            outcome: PublishOutcome.stillProcessing,
          );
          return;
        }
      }
      if (_stale(gen)) return;
      final outcome = switch (status) {
        WorkStatusEnum.published => PublishOutcome.published,
        WorkStatusEnum.blocked => PublishOutcome.blocked,
        WorkStatusEnum.removed => PublishOutcome.removed,
        _ => null,
      };
      if (outcome != null) {
        state = state.copyWith(busy: false, outcome: outcome);
        return;
      }
      if (waited >= pollTimeout) {
        state = state.copyWith(
          busy: false,
          outcome: PublishOutcome.stillProcessing,
        );
        return;
      }
      final d = Duration(seconds: math.min(step++, _maxPollStepSeconds));
      await ref.read(pollDelayProvider)(d);
      if (_stale(gen)) return;
      waited += d;
    }
  }

  // ---- leaving ----

  /// Stops anything in flight and frees the unpublished draft's slot. Does
  /// not wait for the server: the delete runs in the background.
  void discardDraft() {
    _gen++;
    final id = state.workId;
    if (id == null || state.phase == CreatePhase.publishing) return;
    state = state.copyWith(workId: null);
    unawaited(
      _deleteDraft(
        _api,
        ref.read(backgroundRetryDelayProvider),
        id,
        verify: _publishRequested,
      ),
    );
  }

  /// Back to the pick step with a clean slate (only offered with no chapters).
  void changePhoto() {
    if (state.busy || state.pins.isNotEmpty) return;
    discardDraft();
    _uploaded = false;
    _publishRequested = false;
    _uploadUrl = null;
    state = const CreateState();
  }

  /// Stops polling without touching the (already submitted) story.
  void stop() => _gen++;
}

/// Deletes an unpublished draft, retrying once. With [verify] the story is
/// fetched first and left alone unless it is still a draft, because a publish
/// may have gone through. Errors are swallowed; the draft expires on its own
/// after 24 hours. Nothing here is logged.
Future<void> _deleteDraft(
  WorksApi api,
  Future<void> Function(Duration) delay,
  String id, {
  required bool verify,
}) async {
  try {
    if (verify) {
      final res = await apiGuard(() => api.getWork(id: id));
      if (res.data?.data?.status != WorkStatusEnum.draft) return;
    }
    for (var attempt = 0; attempt < 2; attempt++) {
      try {
        await apiGuard(() => api.deleteWork(id: id));
        return;
      } on ApiException catch (e) {
        // A story the server already holds for moderation (pending scan,
        // blocked) is not a draft any more and stays; retrying cannot help.
        if (e.code == 'under_review') return;
        if (attempt == 0) await delay(const Duration(seconds: 3));
      }
    }
  } on ApiException {
    // Leave it be.
  }
}

CreateDraftRequestContentTypeEnum _contentTypeEnum(String type) =>
    switch (type) {
      'image/png' => CreateDraftRequestContentTypeEnum.imageSlashPng,
      'image/webp' => CreateDraftRequestContentTypeEnum.imageSlashWebp,
      _ => CreateDraftRequestContentTypeEnum.imageSlashJpeg,
    };
