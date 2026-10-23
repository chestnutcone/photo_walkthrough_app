import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_exception.dart';
import '../../core/api/api_providers.dart';
import '../create/blob_uploader.dart';
import '../create/create_controller.dart' show maxUploadBytes;
import '../create/create_messages.dart';
import '../create/photo_info.dart';
import '../create/photo_picker.dart';
import '../feed/feed_controller.dart';
import '../portfolio/portfolio_data.dart';
import 'edit_state.dart';
import 'profile_form.dart';

final profileEditProvider =
    NotifierProvider.autoDispose<ProfileEditController, EditState>(
      ProfileEditController.new,
    );

/// Result of [ProfileEditController.save].
class SaveOutcome {
  const SaveOutcome({required this.saved, this.newHandle});
  final bool saved;

  /// Set when the handle changed, so the screen can open the new URL.
  final String? newHandle;
}

/// Edit-profile state for the signed-in account. Rebuilds (discarding edits)
/// when the session changes; the screen only mounts it once `/v1/me` is in.
class ProfileEditController extends Notifier<EditState> {
  @override
  EditState build() {
    ref.watch(sessionEpochProvider);
    final profile = ref.read(meProvider).value?.profile;
    final form = profile == null
        ? const ProfileForm()
        : ProfileForm.fromProfile(profile);
    return EditState(original: form, form: form);
  }

  void edit(ProfileForm Function(ProfileForm) change) {
    final next = change(state.form);
    final handleMoved = next.normalized.handle != state.form.normalized.handle;
    state = state.copyWith(
      form: next,
      clearError: true,
      clearHandleRejected: handleMoved,
    );
  }

  void showErrors() => state = state.copyWith(showErrors: true);

  /// Picks, validates and cleans a profile photo with the same pipeline as
  /// stories (orientation baked in, EXIF/GPS removed).
  Future<void> pickAvatar() async {
    if (state.pickingAvatar || state.saving) return;
    state = state.copyWith(pickingAvatar: true, clearAvatarError: true);
    try {
      final picked = await ref
          .read(photoPickerProvider)
          .pick(PhotoSource.library);
      if (!ref.mounted) return;
      if (picked == null) {
        state = state.copyWith(pickingAvatar: false);
        return;
      }
      state = await _withPhoto(picked);
    } on PhotoAccessDenied {
      if (ref.mounted) {
        state = state.copyWith(
          pickingAvatar: false,
          avatarError: photoAccessDeniedMessage,
        );
      }
    } on Object {
      if (ref.mounted) {
        state = state.copyWith(
          pickingAvatar: false,
          avatarError: "Couldn't open your photos.",
        );
      }
    }
  }

  Future<EditState> _withPhoto(PickedPhoto picked) async {
    final meta = readPhotoMeta(picked.bytes);
    if (meta == null || !supportedTypes.contains(meta.contentType)) {
      return state.copyWith(
        pickingAvatar: false,
        avatarError: unsupportedTypeMessage,
      );
    }
    if (meta.width * meta.height > maxPhotoPixels) {
      return state.copyWith(
        pickingAvatar: false,
        avatarError: tooManyPixelsMessage,
      );
    }
    final bytes = await ref.read(photoNormalizerProvider)(picked.bytes);
    if (bytes.length > maxUploadBytes) {
      return state.copyWith(pickingAvatar: false, avatarError: tooLargeMessage);
    }
    return state.copyWith(
      pickingAvatar: false,
      avatar: NewAvatar(bytes: bytes, contentType: meta.contentType),
    );
  }

  /// Uploads a new avatar (if any), then sends only the changed fields.
  /// Ignored while a save is running. On failure the edits stay.
  Future<SaveOutcome> save() async {
    if (state.saving) return const SaveOutcome(saved: false);
    if (state.fieldErrors.isNotEmpty) {
      state = state.copyWith(showErrors: true);
      return const SaveOutcome(saved: false);
    }
    state = state.copyWith(
      saving: true,
      clearError: true,
      clearAvatarError: true,
    );
    final form = state.form.normalized;
    try {
      final avatar = state.avatar;
      if (avatar != null) await _uploadAvatar(avatar);
      final body = changedFields(
        state.original,
        form,
        avatarUploaded: avatar != null,
      );
      if (body.isNotEmpty) await _patch(body);
    } on ApiException catch (e) {
      return _failed(saveFailure(e, form.handle), form.handle);
    } on BlobUploadException {
      return _failed((
        handle: null,
        form: "Couldn't upload your photo. Try again.",
        locked: false,
        avatar: null,
      ), form.handle);
    }
    if (!ref.mounted) return const SaveOutcome(saved: true);
    final moved = state.handleChanged ? form.handle : null;
    state = state.copyWith(
      original: form,
      form: form,
      clearAvatar: true,
      saving: false,
    );
    _refreshReaders();
    return SaveOutcome(saved: true, newHandle: moved);
  }

  SaveOutcome _failed(
    ({String? handle, String? form, bool locked, String? avatar}) f,
    String attempted,
  ) {
    if (ref.mounted) {
      state = state.copyWith(
        saving: false,
        error: f.form,
        avatarError: f.avatar,
        handleLocked: f.locked ? true : null,
        handleRejected: f.handle == null
            ? null
            : (handle: attempted, message: f.handle!),
      );
    }
    return const SaveOutcome(saved: false);
  }

  Future<void> _uploadAvatar(NewAvatar avatar) async {
    final ticket = unwrapEnvelope(
      success: true,
      data: (await apiGuard(
        () => ref.read(apiProvider).getMeApi().avatarUpload(),
      )).data?.data,
      error: null,
    );
    await ref
        .read(blobUploaderProvider)
        .upload(
          url: Uri.parse(ticket.uploadUrl),
          bytes: avatar.bytes,
          contentType: avatar.contentType,
        );
  }

  /// The generated request model drops explicit nulls (`includeIfNull:
  /// false`), and clearing the public email needs one, so the body is sent
  /// as plain JSON on the same authenticated client.
  Future<void> _patch(Map<String, Object?> body) async {
    await apiGuard(
      () => ref.read(dioProvider).patch<Object?>('/v1/me/profile', data: body),
    );
  }

  /// Portfolio, the viewer's own data and the feed show the old values until
  /// they are asked again.
  void _refreshReaders() {
    ref.invalidate(meProvider);
    ref.invalidate(artistProvider);
    ref.invalidate(worksControllerProvider);
    ref.invalidate(feedControllerProvider);
  }
}
