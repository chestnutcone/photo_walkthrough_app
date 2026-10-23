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
    if (meta.orientation != 1) {
      bytes = await ref.read(photoNormalizerProvider)(bytes);
      if (_stale(gen)) return null;
      meta = readPhotoMeta(bytes) ?? meta;
    }
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
        if (_stale(gen)) return false;
        final made = unwrapEnvelope(
          success: res.data?.success ?? false,
          data: res.data?.data,
          error: res.data?.error,
        );
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
      if (e.expired) await _dropDraft();
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
  Future<void> _dropDraft() async {
    final id = state.workId;
    state = state.copyWith(workId: null);
    _uploaded = false;
    if (id != null) await _deleteQuietly(id);
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
  Future<Work?> _save(int gen) async {
    final id = state.workId!;
    await apiGuard(
      () => _api.updateWork(id: id, updateWorkRequest: buildUpdateRequest()),
    );
    if (_stale(gen)) return null;
    try {
      final res = await apiGuard(() => _api.publishWork(id: id));
      if (_stale(gen)) return null;
      final work = unwrapEnvelope(
        success: res.data?.success ?? false,
        data: res.data?.data,
        error: res.data?.error,
      );
      return work;
    } on ApiException catch (e) {
      // A lost response to a publish that succeeded: carry on to polling.
      if (e.code != 'not_editable') rethrow;
      final work = await _fetch(id);
      if (_stale(gen)) return null;
      if (work.status == WorkStatusEnum.draft) rethrow;
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
        if (!transient) rethrow;
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

  /// Stops anything in flight and frees the unpublished draft's slot.
  Future<void> discardDraft() async {
    _gen++;
    final id = state.workId;
    final inReview = state.phase == CreatePhase.publishing;
    if (id == null || inReview) return;
    state = state.copyWith(workId: null);
    await _deleteQuietly(id);
  }

  /// Stops polling without touching the (already submitted) story.
  void stop() => _gen++;

  Future<void> _deleteQuietly(String id) async {
    try {
      await apiGuard(() => _api.deleteWork(id: id));
    } on ApiException {
      // The draft expires on its own after 24 hours.
    }
  }
}

CreateDraftRequestContentTypeEnum _contentTypeEnum(String type) =>
    switch (type) {
      'image/png' => CreateDraftRequestContentTypeEnum.imageSlashPng,
      'image/webp' => CreateDraftRequestContentTypeEnum.imageSlashWebp,
      _ => CreateDraftRequestContentTypeEnum.imageSlashJpeg,
    };
