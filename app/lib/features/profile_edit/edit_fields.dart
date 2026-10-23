import 'package:flutter/material.dart';

import '../../core/theme/tokens.dart';
import '../onboarding/handle_availability.dart';
import '../portfolio/avatar.dart';
import 'edit_state.dart';
import 'profile_form.dart';

const publicEmailNote =
    'Shown publicly on your profile. Your sign-in email is never shown.';

/// Small caps label above a field, with spacing below.
class LabeledField extends StatelessWidget {
  const LabeledField({super.key, required this.label, required this.child});
  final String label;
  final Widget child;

  @override
  Widget build(BuildContext context) => Padding(
    padding: const EdgeInsets.only(bottom: 18),
    child: Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Padding(
          padding: const EdgeInsets.only(bottom: 6),
          child: Text(label, style: VelaText.monoKicker),
        ),
        child,
      ],
    ),
  );
}

/// "123/280", red when over. The remaining count is announced.
class BioCounter extends StatelessWidget {
  const BioCounter({super.key, required this.text});
  final String text;

  @override
  Widget build(BuildContext context) {
    final n = codePointCount(text);
    final over = n > bioMax;
    return Align(
      alignment: Alignment.centerRight,
      child: Semantics(
        liveRegion: over,
        label: over
            ? 'Bio is ${n - bioMax} characters over the $bioMax limit'
            : '$n of $bioMax characters used',
        excludeSemantics: true,
        child: Padding(
          padding: const EdgeInsets.only(top: 4),
          child: Text(
            '$n/$bioMax',
            key: const ValueKey('bio-counter'),
            style: VelaText.mono11.copyWith(
              color: over ? VelaColors.danger : VelaColors.muted,
            ),
          ),
        ),
      ),
    );
  }
}

/// Availability / lock / rejection text under the handle field.
class HandleStatusLine extends StatelessWidget {
  const HandleStatusLine({super.key, required this.state, required this.check});
  final EditState state;
  final HandleCheck check;

  @override
  Widget build(BuildContext context) {
    final problem = state.handleMessage(check);
    final String? text;
    var color = VelaColors.muted;
    if (problem != null) {
      text = problem;
      color = VelaColors.danger;
    } else if (!state.handleChanged) {
      text = 'You can change your handle once every 30 days.';
    } else if (check.handle != state.form.normalized.handle ||
        check.status == HandleStatus.checking) {
      text = 'Checking...';
    } else if (check.status == HandleStatus.available) {
      text = '@${check.handle} is available.';
      color = VelaColors.accent;
    } else if (check.status == HandleStatus.unknown) {
      text = "Couldn't check right now. Edit the handle to try again.";
      color = VelaColors.danger;
    } else {
      text = null;
    }
    if (text == null) return const SizedBox.shrink();
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

/// Current or newly picked photo with a change button (one 88pt target).
class AvatarEditor extends StatelessWidget {
  const AvatarEditor({
    super.key,
    required this.state,
    required this.name,
    required this.onTap,
    this.currentUrl,
  });

  final EditState state;
  final String name;
  final String? currentUrl;
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) {
    final picked = state.avatar;
    return Row(
      children: [
        Semantics(
          button: true,
          label: 'Change profile photo',
          excludeSemantics: true,
          onTap: onTap,
          child: InkWell(
            key: const ValueKey('edit-avatar'),
            customBorder: const CircleBorder(),
            onTap: onTap,
            child: SizedBox(
              width: 88,
              height: 88,
              child: picked == null
                  ? Avatar(name: name, size: 88, url: currentUrl)
                  : ClipOval(
                      child: Image.memory(picked.bytes, fit: BoxFit.cover),
                    ),
            ),
          ),
        ),
        const SizedBox(width: 16),
        Expanded(
          child: Semantics(
            liveRegion: state.avatarError != null,
            child: Text(
              state.avatarError ??
                  (picked == null
                      ? 'Profile photo\nTap to change it.'
                      : 'New photo ready.\nIt is uploaded when you save.'),
              key: const ValueKey('edit-avatar-note'),
              style: VelaText.sans13.copyWith(
                color: state.avatarError == null
                    ? VelaColors.muted
                    : VelaColors.danger,
              ),
            ),
          ),
        ),
      ],
    );
  }
}
