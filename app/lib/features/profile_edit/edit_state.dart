import 'package:flutter/foundation.dart';

import '../../core/api/api_exception.dart';
import '../onboarding/handle_availability.dart';
import 'profile_form.dart';

/// A prepared avatar (orientation baked in, metadata stripped), waiting to
/// be uploaded with the next save.
@immutable
class NewAvatar {
  const NewAvatar({required this.bytes, required this.contentType});
  final Uint8List bytes;
  final String contentType;
}

@immutable
class EditState {
  const EditState({
    required this.original,
    required this.form,
    this.avatar,
    this.saving = false,
    this.pickingAvatar = false,
    this.error,
    this.avatarError,
    this.handleRejected,
    this.handleLocked = false,
    this.showErrors = false,
  });

  final ProfileForm original;
  final ProfileForm form;
  final NewAvatar? avatar;
  final bool saving;
  final bool pickingAvatar;

  /// Form-level failure of the last save; the edits are kept.
  final String? error;
  final String? avatarError;

  /// A handle the server refused (taken or invalid) and the text to show.
  final ({String handle, String message})? handleRejected;

  /// The server said the handle was changed too recently.
  final bool handleLocked;
  final bool showErrors;

  bool get dirty => form.normalized != original.normalized || avatar != null;

  bool get handleChanged => form.normalized.handle != original.handle;

  Map<String, String> get fieldErrors => validateForm(form);

  /// Message for the handle field, or null when it is fine so far.
  String? handleMessage(HandleCheck check) {
    final f = form.normalized;
    final local = fieldErrors['handle'];
    if (local != null) {
      return local;
    }
    if (!handleChanged) {
      return null;
    }
    if (handleLocked) {
      return lockedMessage;
    }
    final rejected = handleRejected;
    if (rejected != null && rejected.handle == f.handle) {
      return rejected.message;
    }
    if (check.handle == f.handle && check.status == HandleStatus.taken) {
      return 'That handle is already taken.';
    }
    return null;
  }

  /// Whether Save may be pressed. A changed handle must be confirmed free
  /// for exactly the text now in the field (answers for older text are
  /// ignored), and the lock must not have been reported.
  bool canSave(HandleCheck check) {
    if (saving || !dirty || fieldErrors.isNotEmpty) {
      return false;
    }
    if (!handleChanged) {
      return true;
    }
    if (handleLocked || handleMessage(check) != null) {
      return false;
    }
    return check.handle == form.normalized.handle &&
        check.status == HandleStatus.available;
  }

  EditState copyWith({
    ProfileForm? original,
    ProfileForm? form,
    NewAvatar? avatar,
    bool clearAvatar = false,
    bool? saving,
    bool? pickingAvatar,
    String? error,
    bool clearError = false,
    String? avatarError,
    bool clearAvatarError = false,
    ({String handle, String message})? handleRejected,
    bool clearHandleRejected = false,
    bool? handleLocked,
    bool? showErrors,
  }) => EditState(
    original: original ?? this.original,
    form: form ?? this.form,
    avatar: clearAvatar ? null : avatar ?? this.avatar,
    saving: saving ?? this.saving,
    pickingAvatar: pickingAvatar ?? this.pickingAvatar,
    error: clearError ? null : error ?? this.error,
    avatarError: clearAvatarError ? null : avatarError ?? this.avatarError,
    handleRejected: clearHandleRejected
        ? null
        : handleRejected ?? this.handleRejected,
    handleLocked: handleLocked ?? this.handleLocked,
    showErrors: showErrors ?? this.showErrors,
  );
}

const lockedMessage =
    'You changed your handle recently. You can change it once every 30 days.';

/// What a failed save means for the screen. Never echoes server text except
/// the validation message, which names the field and not the user's text.
({String? handle, String? form, bool locked, String? avatar}) saveFailure(
  ApiException e,
  String attemptedHandle,
) {
  switch (e.code) {
    case 'handle_taken':
      return (
        handle: 'That handle is already taken.',
        form: null,
        locked: false,
        avatar: null,
      );
    case 'invalid_handle':
      return (
        handle:
            'Use 3-20 characters: lowercase letters, numbers and underscores.',
        form: null,
        locked: false,
        avatar: null,
      );
    case 'handle_change_too_soon':
    case 'handle_locked':
      return (handle: null, form: null, locked: true, avatar: null);
    case 'invalid_image':
      return (
        handle: null,
        form: null,
        locked: false,
        avatar: "That photo couldn't be used. Choose a JPEG, PNG or WebP.",
      );
  }
  final text = switch (e.code) {
    'network_error' =>
      "Can't reach the server. Your edits are kept; try again.",
    'rate_limited' => 'Too many tries. Wait a moment and save again.',
    'validation_failed' when e.message.isNotEmpty => 'Not saved: ${e.message}',
    _ => "Couldn't save your profile. Your edits are kept; try again.",
  };
  return (handle: null, form: text, locked: false, avatar: null);
}
