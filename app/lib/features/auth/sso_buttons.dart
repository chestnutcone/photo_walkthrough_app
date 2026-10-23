import 'package:flutter/material.dart';

import '../../core/theme/form_styles.dart';
import '../../core/theme/tokens.dart';

/// The three ways in. Apple is passed [onApple] only where it is offered.
class SsoButtons extends StatelessWidget {
  const SsoButtons({
    super.key,
    required this.onGoogle,
    required this.onEmail,
    this.onApple,
    this.enabled = true,
  });

  final VoidCallback? onApple;
  final VoidCallback onGoogle;
  final VoidCallback onEmail;
  final bool enabled;

  @override
  Widget build(BuildContext context) => Column(
    crossAxisAlignment: CrossAxisAlignment.stretch,
    children: [
      if (onApple != null) ...[
        FilledButton.icon(
          key: const ValueKey('sso-apple'),
          style: velaFilledButtonStyle().copyWith(
            minimumSize: const WidgetStatePropertyAll(Size(44, 52)),
          ),
          onPressed: enabled ? onApple : null,
          icon: const Icon(Icons.apple, size: 22),
          label: const Text('Continue with Apple'),
        ),
        const SizedBox(height: 10),
      ],
      OutlinedButton.icon(
        key: const ValueKey('sso-google'),
        style: velaOutlinedButtonStyle(),
        onPressed: enabled ? onGoogle : null,
        icon: Text('G', style: VelaText.serif24.copyWith(height: 1)),
        label: const Text('Continue with Google'),
      ),
      const SizedBox(height: 10),
      OutlinedButton.icon(
        key: const ValueKey('sso-email'),
        style: velaOutlinedButtonStyle(),
        onPressed: enabled ? onEmail : null,
        icon: const Icon(Icons.mail_outline, size: 20),
        label: const Text('Continue with email'),
      ),
    ],
  );
}
