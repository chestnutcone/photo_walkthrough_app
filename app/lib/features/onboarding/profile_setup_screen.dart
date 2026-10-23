import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/theme/form_styles.dart';
import '../../core/theme/tokens.dart';
import 'avatar_picker.dart';
import 'handle_availability.dart';
import 'handle_rules.dart';
import 'legal_links.dart';
import 'onboarding_draft.dart';
import 'onboarding_widgets.dart';

/// Lower-cases, and drops spaces and "@", as the user types or pastes.
class _HandleFormatter extends TextInputFormatter {
  @override
  TextEditingValue formatEditUpdate(
    TextEditingValue oldValue,
    TextEditingValue newValue,
  ) {
    final text = newValue.text.replaceAll(RegExp(r'[\s@]'), '').toLowerCase();
    if (text == newValue.text) return newValue;
    return TextEditingValue(
      text: text,
      selection: TextSelection.collapsed(offset: text.length),
    );
  }
}

/// Step 1 of 2: who you are on the app. Nothing is sent until the follow
/// step, because the API takes the whole profile in one call.
class ProfileSetupScreen extends ConsumerStatefulWidget {
  const ProfileSetupScreen({super.key});

  @override
  ConsumerState<ProfileSetupScreen> createState() => _ProfileSetupState();
}

class _ProfileSetupState extends ConsumerState<ProfileSetupScreen> {
  late final TextEditingController _name;
  late final TextEditingController _handle;

  @override
  void initState() {
    super.initState();
    final draft = ref.read(onboardingDraftProvider);
    _name = TextEditingController(text: draft.displayName);
    _handle = TextEditingController(text: draft.handle);
    if (draft.handle.isNotEmpty) {
      // Coming back from the next step: the check was discarded with the
      // screen, so ask again.
      WidgetsBinding.instance.addPostFrameCallback((_) {
        if (!mounted) return;
        ref
            .read(handleAvailabilityProvider.notifier)
            .onChanged(draft.handle, debounce: Duration.zero);
      });
    }
  }

  @override
  void dispose() {
    _name.dispose();
    _handle.dispose();
    super.dispose();
  }

  Future<void> _pickAvatar() async {
    final messenger = ScaffoldMessenger.of(context);
    try {
      final picked = await ref.read(avatarPickerProvider).pick();
      if (picked != null && mounted) {
        ref.read(onboardingDraftProvider.notifier).setAvatar(picked);
      }
    } on Object {
      messenger.showSnackBar(
        const SnackBar(content: Text("Couldn't open your photos.")),
      );
    }
  }

  @override
  Widget build(BuildContext context) {
    final draft = ref.watch(onboardingDraftProvider);
    final check = ref.watch(handleAvailabilityProvider);
    final notifier = ref.read(onboardingDraftProvider.notifier);
    final open = ref.read(legalLinkOpenerProvider);
    return StepLayout(
      kicker: 'STEP 1 OF 2',
      backKey: const ValueKey('profile-back'),
      onBack: () => notifier.goTo(OnboardingStep.welcome),
      footer: FilledButton(
        key: const ValueKey('profile-continue'),
        style: velaFilledButtonStyle().copyWith(
          minimumSize: const WidgetStatePropertyAll(Size(44, 52)),
        ),
        onPressed: draft.canContinue(check)
            ? () => notifier.goTo(OnboardingStep.follow)
            : null,
        child: const Text('Continue'),
      ),
      children: [
        Semantics(
          header: true,
          child: Text('Your profile', style: VelaText.serif36),
        ),
        const SizedBox(height: 18),
        _AvatarButton(avatar: draft.avatar, onTap: _pickAvatar),
        const SizedBox(height: 20),
        _label('DISPLAY NAME'),
        TextField(
          key: const ValueKey('name-field'),
          controller: _name,
          textCapitalization: TextCapitalization.words,
          textInputAction: TextInputAction.next,
          autofillHints: const [AutofillHints.name],
          inputFormatters: [
            LengthLimitingTextInputFormatter(maxDisplayNameLength),
          ],
          style: VelaText.sans15,
          decoration: velaInputDecoration(hint: 'Your name'),
          onChanged: notifier.setName,
        ),
        const SizedBox(height: 18),
        _label('HANDLE'),
        TextField(
          key: const ValueKey('handle-field'),
          controller: _handle,
          autocorrect: false,
          enableSuggestions: false,
          textInputAction: TextInputAction.done,
          keyboardType: TextInputType.visiblePassword,
          inputFormatters: [
            _HandleFormatter(),
            LengthLimitingTextInputFormatter(HandleRules.maxLength + 1),
          ],
          style: VelaText.sans15,
          decoration: velaInputDecoration(hint: 'yourname').copyWith(
            prefixText: '@',
            prefixStyle: VelaText.sans15.copyWith(color: VelaColors.muted),
          ),
          onChanged: (v) {
            notifier.setHandle(v);
            ref.read(handleAvailabilityProvider.notifier).onChanged(v);
          },
        ),
        const SizedBox(height: 8),
        _HandleStatus(
          check: check,
          rejected: draft.handleRejected,
          rejectedAsInvalid: draft.rejectedAsInvalid,
        ),
        const SizedBox(height: 4),
        Text(
          'You can change your handle once every 30 days.',
          style: VelaText.sans12.copyWith(color: VelaColors.muted),
        ),
        const SizedBox(height: 22),
        LabeledCheck(
          key: const ValueKey('age-check'),
          value: draft.ageConfirmed,
          onChanged: notifier.setAge,
          label: 'I confirm that I am 18 or older.',
        ),
        LabeledCheck(
          key: const ValueKey('terms-check'),
          value: draft.termsAccepted,
          onChanged: notifier.setTerms,
          label:
              'I agree to the Terms of Service and the Privacy Policy, and '
              'I will only upload photographs I own or have the right to share.',
        ),
        Wrap(
          children: [
            TextButton(
              key: const ValueKey('terms-link'),
              style: velaTextButtonStyle(),
              onPressed: () => open(context, legalUri(LegalDoc.terms)),
              child: const Text('Read the Terms of Service'),
            ),
            TextButton(
              key: const ValueKey('privacy-link'),
              style: velaTextButtonStyle(),
              onPressed: () => open(context, legalUri(LegalDoc.privacy)),
              child: const Text('Read the Privacy Policy'),
            ),
          ],
        ),
      ],
    );
  }

  Widget _label(String text) => Padding(
    padding: const EdgeInsets.only(bottom: 8),
    child: Text(text, style: VelaText.mono11),
  );
}

class _HandleStatus extends StatelessWidget {
  const _HandleStatus({
    required this.check,
    required this.rejected,
    required this.rejectedAsInvalid,
  });

  final HandleCheck check;
  final bool rejected;
  final bool rejectedAsInvalid;

  @override
  Widget build(BuildContext context) {
    final (String text, Color color) = rejected
        ? (
            rejectedAsInvalid
                ? 'Use 3 to 20 lowercase letters, numbers or underscores.'
                : 'That handle is already taken. Try another.',
            VelaColors.danger,
          )
        : switch (check.status) {
            HandleStatus.empty => (
              '3 to 20 characters: lowercase letters, numbers, underscores.',
              VelaColors.muted,
            ),
            HandleStatus.invalid => (
              HandleRules.message(check.problem!),
              VelaColors.danger,
            ),
            HandleStatus.checking => ('Checking…', VelaColors.muted),
            HandleStatus.available => (
              '@${check.handle} is available.',
              VelaColors.accent,
            ),
            HandleStatus.taken => (
              'That handle is already taken. Try another.',
              VelaColors.danger,
            ),
            HandleStatus.unknown => (
              "Couldn't check right now. Edit the handle to try again.",
              VelaColors.danger,
            ),
          };
    return Semantics(
      liveRegion: true,
      child: Text(
        text,
        key: const ValueKey('handle-status'),
        style: VelaText.sans13.copyWith(color: color),
      ),
    );
  }
}

class _AvatarButton extends StatelessWidget {
  const _AvatarButton({required this.avatar, required this.onTap});

  final PickedAvatar? avatar;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) => Row(
    children: [
      Semantics(
        button: true,
        label: avatar == null
            ? 'Add a profile photo, optional'
            : 'Change profile photo',
        excludeSemantics: true,
        child: InkWell(
          key: const ValueKey('avatar-button'),
          customBorder: const CircleBorder(),
          onTap: onTap,
          child: Container(
            width: 88,
            height: 88,
            clipBehavior: Clip.antiAlias,
            decoration: BoxDecoration(
              shape: BoxShape.circle,
              color: VelaColors.input,
              border: Border.all(color: VelaColors.outline),
            ),
            child: avatar == null
                ? const Icon(
                    Icons.add_a_photo_outlined,
                    color: VelaColors.muted,
                  )
                : Image.memory(avatar!.bytes, fit: BoxFit.cover),
          ),
        ),
      ),
      const SizedBox(width: 16),
      Expanded(
        child: Text(
          'Profile photo\nOptional. You can add one later.',
          style: VelaText.sans13.copyWith(color: VelaColors.muted),
        ),
      ),
    ],
  );
}
