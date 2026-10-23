import 'package:flutter/material.dart';

import '../../core/theme/tokens.dart';

/// Layout shared by the onboarding steps: scrolling content that stays clear
/// of the keyboard, with the actions pinned underneath.
class StepLayout extends StatelessWidget {
  const StepLayout({
    super.key,
    required this.children,
    required this.footer,
    this.kicker,
    this.onBack,
    this.backKey,
  });

  final String? kicker;
  final VoidCallback? onBack;
  final Key? backKey;
  final List<Widget> children;
  final Widget footer;

  @override
  Widget build(BuildContext context) => SafeArea(
    child: Column(
      children: [
        SizedBox(
          height: 52,
          child: Row(
            children: [
              if (onBack != null)
                IconButton(
                  key: backKey,
                  tooltip: 'Back',
                  constraints: const BoxConstraints(
                    minWidth: 48,
                    minHeight: 48,
                  ),
                  onPressed: onBack,
                  icon: const Icon(Icons.arrow_back, color: VelaColors.ink),
                )
              else
                const SizedBox(width: 20),
              if (kicker != null) Text(kicker!, style: VelaText.monoKicker),
            ],
          ),
        ),
        Expanded(
          child: SingleChildScrollView(
            keyboardDismissBehavior: ScrollViewKeyboardDismissBehavior.onDrag,
            padding: const EdgeInsets.fromLTRB(20, 4, 20, 24),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: children,
            ),
          ),
        ),
        Padding(
          padding: const EdgeInsets.fromLTRB(20, 8, 20, 12),
          child: footer,
        ),
      ],
    ),
  );
}

/// Checkbox with a label that is one 48pt-tall target and one semantic node.
class LabeledCheck extends StatelessWidget {
  const LabeledCheck({
    super.key,
    required this.value,
    required this.onChanged,
    required this.label,
  });

  final bool value;
  final ValueChanged<bool> onChanged;
  final String label;

  @override
  Widget build(BuildContext context) => Semantics(
    checked: value,
    inMutuallyExclusiveGroup: false,
    label: label,
    excludeSemantics: true,
    onTap: () => onChanged(!value),
    child: InkWell(
      borderRadius: BorderRadius.circular(10),
      onTap: () => onChanged(!value),
      child: ConstrainedBox(
        constraints: const BoxConstraints(minHeight: 48),
        child: Row(
          crossAxisAlignment: CrossAxisAlignment.center,
          children: [
            Checkbox(
              value: value,
              onChanged: (v) => onChanged(v ?? false),
              activeColor: VelaColors.accent,
              checkColor: VelaColors.background,
              side: const BorderSide(color: VelaColors.faint, width: 1.5),
            ),
            Expanded(
              child: Padding(
                padding: const EdgeInsets.symmetric(vertical: 8),
                child: Text(
                  label,
                  style: VelaText.sans14.copyWith(color: VelaColors.secondary),
                ),
              ),
            ),
          ],
        ),
      ),
    ),
  );
}

const accountNote =
    'Each sign-in method is its own account. Signing in with Apple, Google '
    'and email gives you three separate accounts; they are never merged.';
