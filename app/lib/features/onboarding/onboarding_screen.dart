import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import 'follow_suggestions_screen.dart';
import 'onboarding_draft.dart';
import 'profile_setup_screen.dart';
import 'welcome_screen.dart';

/// The `/onboarding` route: welcome, profile, follow suggestions. The step
/// lives in [onboardingDraftProvider] so it survives the sample story, which
/// is a separate route on top.
class OnboardingScreen extends ConsumerWidget {
  const OnboardingScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final step = ref.watch(onboardingDraftProvider.select((d) => d.step));
    return PopScope(
      // Back walks one step; only from the welcome step does it leave.
      canPop: step == OnboardingStep.welcome,
      onPopInvokedWithResult: (didPop, _) {
        if (didPop) return;
        final notifier = ref.read(onboardingDraftProvider.notifier);
        notifier.goTo(
          step == OnboardingStep.follow
              ? OnboardingStep.profile
              : OnboardingStep.welcome,
        );
      },
      child: Scaffold(
        body: AnimatedSwitcher(
          duration: const Duration(milliseconds: 220),
          child: KeyedSubtree(
            key: ValueKey(step),
            child: switch (step) {
              OnboardingStep.welcome => const WelcomeScreen(),
              OnboardingStep.profile => const ProfileSetupScreen(),
              OnboardingStep.follow => const FollowSuggestionsScreen(),
            },
          ),
        ),
      ),
    );
  }
}
