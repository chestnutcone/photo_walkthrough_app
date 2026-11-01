import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/auth/auth_controller.dart';
import '../portfolio/portfolio_data.dart';

/// True when the last sign-in said the account is new (`isNewUser`). It only
/// covers the gap while `/v1/me` cannot be read; `/v1/me` is authoritative.
/// Resets on every sign-in or sign-out.
final newUserHintProvider = NotifierProvider<NewUserHint, bool>(
  NewUserHint.new,
);

class NewUserHint extends Notifier<bool> {
  @override
  bool build() {
    ref.watch(sessionEpochProvider);
    return false;
  }

  void set(bool value) => state = value;
}

/// Set once `completeOnboarding` succeeded in this session, so a failed or
/// stale `/v1/me` refresh cannot send the user back to onboarding. Resets on
/// every sign-in or sign-out.
final onboardedLocallyProvider = NotifierProvider<OnboardedLocally, bool>(
  OnboardedLocally.new,
);

class OnboardedLocally extends Notifier<bool> {
  @override
  bool build() {
    ref.watch(sessionEpochProvider);
    return false;
  }

  void set(bool value) => state = value;
}

/// Whether the signed-in account has to finish onboarding. Guests never do.
bool deriveNeedsOnboarding(Ref ref) {
  final signedIn = ref.watch(sessionProvider.select((s) => s.value != null));
  if (!signedIn) return false;
  if (ref.watch(onboardedLocallyProvider)) return false;
  final me = ref.watch(meProvider).value;
  if (me != null) return !me.onboardingComplete;
  return ref.watch(newUserHintProvider);
}
