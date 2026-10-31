import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_providers.dart' show sessionEpochProvider;

/// Where a new account was heading (for example an invite link to a story)
/// when the router sent it to onboarding. Finishing onboarding goes there
/// instead of the feed. A new instance per session epoch, so a link
/// remembered for one account never applies to the next.
class OnboardingReturn {
  String? _location;

  void remember(String location) => _location = location;

  /// The remembered location, once.
  String? take() {
    final l = _location;
    _location = null;
    return l;
  }
}

final onboardingReturnProvider = Provider<OnboardingReturn>((ref) {
  ref.watch(sessionEpochProvider);
  return OnboardingReturn();
});
