import 'package:flutter/foundation.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/auth/auth_controller.dart';
import 'avatar_picker.dart';
import 'handle_availability.dart';
import 'handle_rules.dart';

enum OnboardingStep { welcome, profile, follow }

const maxDisplayNameLength = 60;

/// Everything entered so far. Lives in a provider so the answers survive
/// going back a step or watching the demo story.
@immutable
class OnboardingDraft {
  const OnboardingDraft({
    this.step = OnboardingStep.welcome,
    this.displayName = '',
    this.handle = '',
    this.avatar,
    this.ageConfirmed = false,
    this.termsAccepted = false,
    this.rejectedHandle,
    this.rejectedAsInvalid = false,
    this.follows = const {},
  });

  final OnboardingStep step;
  final String displayName;
  final String handle;
  final PickedAvatar? avatar;
  final bool ageConfirmed;
  final bool termsAccepted;

  /// A handle the server refused at submit time (taken or invalid).
  final String? rejectedHandle;

  /// The refusal was a format problem (400 invalid_handle), not a clash.
  final bool rejectedAsInvalid;
  final Set<String> follows;

  bool get handleRejected => handle.isNotEmpty && handle == rejectedHandle;

  /// The profile step can be submitted: name, a free handle and both
  /// confirmations.
  bool canContinue(HandleCheck check) {
    final name = displayName.trim();
    return name.isNotEmpty &&
        name.length <= maxDisplayNameLength &&
        ageConfirmed &&
        termsAccepted &&
        !handleRejected &&
        check.handle == handle &&
        check.status == HandleStatus.available;
  }

  OnboardingDraft copyWith({
    OnboardingStep? step,
    String? displayName,
    String? handle,
    Object? avatar = _keep,
    bool? ageConfirmed,
    bool? termsAccepted,
    Object? rejectedHandle = _keep,
    bool? rejectedAsInvalid,
    Set<String>? follows,
  }) => OnboardingDraft(
    step: step ?? this.step,
    displayName: displayName ?? this.displayName,
    handle: handle ?? this.handle,
    avatar: identical(avatar, _keep) ? this.avatar : avatar as PickedAvatar?,
    ageConfirmed: ageConfirmed ?? this.ageConfirmed,
    termsAccepted: termsAccepted ?? this.termsAccepted,
    rejectedHandle: identical(rejectedHandle, _keep)
        ? this.rejectedHandle
        : rejectedHandle as String?,
    rejectedAsInvalid: rejectedAsInvalid ?? this.rejectedAsInvalid,
    follows: follows ?? this.follows,
  );
}

const Object _keep = Object();

final onboardingDraftProvider =
    NotifierProvider<OnboardingDraftNotifier, OnboardingDraft>(
      OnboardingDraftNotifier.new,
    );

class OnboardingDraftNotifier extends Notifier<OnboardingDraft> {
  @override
  OnboardingDraft build() {
    ref.watch(sessionEpochProvider); // a different account starts fresh
    return const OnboardingDraft();
  }

  void goTo(OnboardingStep step) => state = state.copyWith(step: step);
  void setName(String v) => state = state.copyWith(displayName: v);
  void setHandle(String raw) =>
      state = state.copyWith(handle: HandleRules.normalize(raw));
  void setAvatar(PickedAvatar? a) => state = state.copyWith(avatar: a);
  void setAge(bool v) => state = state.copyWith(ageConfirmed: v);
  void setTerms(bool v) => state = state.copyWith(termsAccepted: v);

  void rejectHandle(String handle, {bool invalid = false}) =>
      state = state.copyWith(
        rejectedHandle: handle,
        rejectedAsInvalid: invalid,
        step: OnboardingStep.profile,
      );

  void toggleFollow(String id) {
    final next = {...state.follows};
    if (!next.remove(id)) next.add(id);
    state = state.copyWith(follows: next);
  }
}
