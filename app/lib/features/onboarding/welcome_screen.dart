import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/branding.dart';
import '../../core/theme/form_styles.dart';
import '../../core/theme/tokens.dart';
import 'onboarding_draft.dart';
import 'onboarding_widgets.dart';

/// First onboarding step: what the app is, an optional sample story, then on
/// to the profile. There is no notification prompt anywhere in onboarding.
class WelcomeScreen extends ConsumerWidget {
  const WelcomeScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) => StepLayout(
    kicker: 'WELCOME TO ${Branding.appName.toUpperCase()}',
    footer: Column(
      mainAxisSize: MainAxisSize.min,
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        FilledButton(
          key: const ValueKey('welcome-continue'),
          style: velaFilledButtonStyle().copyWith(
            minimumSize: const WidgetStatePropertyAll(Size(44, 52)),
          ),
          onPressed: () => ref
              .read(onboardingDraftProvider.notifier)
              .goTo(OnboardingStep.profile),
          child: const Text('Set up my profile'),
        ),
        const SizedBox(height: 4),
        TextButton(
          key: const ValueKey('welcome-demo'),
          style: velaTextButtonStyle(),
          onPressed: () => context.push('/onboarding/demo'),
          child: const Text('Watch a short sample story first'),
        ),
      ],
    ),
    children: [
      const SizedBox(height: 8),
      Semantics(
        header: true,
        child: Text(
          'Photographs, with the story left in.',
          style: VelaText.serif46,
        ),
      ),
      const SizedBox(height: 18),
      const _SampleCard(),
      const SizedBox(height: 20),
      Text(
        'Pin notes to the details of a photograph and walk people through '
        'them, one chapter at a time.',
        style: VelaText.sans15.copyWith(color: VelaColors.secondary),
      ),
      const SizedBox(height: 14),
      Text(
        accountNote,
        style: VelaText.sans12.copyWith(color: VelaColors.muted, height: 1.45),
      ),
    ],
  );
}

/// The sample photograph with two accent pins: a hint of the app, built from
/// the bundled original artwork.
class _SampleCard extends StatelessWidget {
  const _SampleCard();

  @override
  Widget build(BuildContext context) => ExcludeSemantics(
    child: ClipRRect(
      borderRadius: BorderRadius.circular(14),
      child: AspectRatio(
        aspectRatio: 358 / 220,
        child: Stack(
          fit: StackFit.expand,
          children: [
            const Image(
              image: AssetImage('assets/demo/demo_photo.png'),
              fit: BoxFit.cover,
              alignment: Alignment(0.2, 0.2),
            ),
            for (final p in const [Offset(0.30, 0.50), Offset(0.62, 0.20)])
              Align(
                alignment: Alignment(p.dx * 2 - 1, p.dy * 2 - 1),
                child: const _Pin(),
              ),
          ],
        ),
      ),
    ),
  );
}

class _Pin extends StatelessWidget {
  const _Pin();

  @override
  Widget build(BuildContext context) => Container(
    width: 22,
    height: 22,
    decoration: BoxDecoration(
      shape: BoxShape.circle,
      color: VelaColors.accent,
      border: Border.all(
        color: VelaColors.accent.withValues(alpha: 0.25),
        width: 5,
      ),
    ),
  );
}
