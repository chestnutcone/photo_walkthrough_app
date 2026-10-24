import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/config.dart';
import '../../core/require_sign_in.dart';
import '../../core/theme/form_styles.dart';
import '../../core/theme/tokens.dart';
import '../onboarding/legal_links.dart';

/// Route the router sends everyone to while [accountBlockProvider] is set.
const suspendedPath = '/suspended';

class SuspendedCopy {
  const SuspendedCopy(this.title, this.body);
  final String title;
  final String body;

  static SuspendedCopy of(AccountBlock block) => switch (block) {
    AccountBlock.suspended => const SuspendedCopy(
      'Your account is suspended',
      "You can't use this account right now. If you think this is a "
          "mistake, write to us and we'll take a look.",
    ),
    AccountBlock.deleted => const SuspendedCopy(
      'This account has been deleted',
      'Everything tied to it has been removed. You can sign in with '
          'another account or keep browsing as a guest.',
    ),
  };
}

/// Shown when the server ended the account (403 `account_suspended` or
/// `account_deleted`). The session is already cleared, so leaving this screen
/// (as a guest, or by signing in with another account) is always possible.
class SuspendedScreen extends ConsumerStatefulWidget {
  const SuspendedScreen({super.key});

  @override
  ConsumerState<SuspendedScreen> createState() => _SuspendedScreenState();
}

class _SuspendedScreenState extends ConsumerState<SuspendedScreen> {
  @override
  void initState() {
    super.initState();
    // A sheet or dialog that was open when the 403 arrived must not stay on
    // top of this screen.
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (!mounted) return;
      Navigator.of(
        context,
        rootNavigator: true,
      ).popUntil((route) => route is! PopupRoute);
    });
  }

  Future<void> _contactSupport() async {
    final messenger = ScaffoldMessenger.of(context);
    final uri = Uri(scheme: 'mailto', path: AppConfig.supportEmail);
    if (await ref.read(legalUrlLauncherProvider)(uri)) return;
    messenger.showSnackBar(
      SnackBar(
        content: Text(
          'No email app found. Write to ${AppConfig.supportEmail}.',
        ),
      ),
    );
  }

  /// A successful sign-in clears the block (SessionNotifier.signIn), and the
  /// router then leaves this screen.
  Future<void> _signInAgain() => ref.read(signInSheetLauncherProvider)(context);

  void _continueAsGuest() => ref.read(accountBlockProvider.notifier).set(null);

  @override
  Widget build(BuildContext context) {
    final block = ref.watch(accountBlockProvider);
    if (block == null) return const Scaffold();
    final copy = SuspendedCopy.of(block);
    return Scaffold(
      backgroundColor: VelaColors.background,
      body: SafeArea(
        child: Center(
          child: ConstrainedBox(
            constraints: const BoxConstraints(maxWidth: 420),
            child: SingleChildScrollView(
              padding: const EdgeInsets.all(24),
              child: Column(
                mainAxisSize: MainAxisSize.min,
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Semantics(
                    header: true,
                    child: Text(copy.title, style: VelaText.serif34),
                  ),
                  const SizedBox(height: 14),
                  Text(
                    copy.body,
                    style: VelaText.sans15.copyWith(
                      color: VelaColors.secondary,
                    ),
                  ),
                  const SizedBox(height: 18),
                  Text(
                    AppConfig.supportEmail,
                    style: VelaText.sans14m.copyWith(color: VelaColors.accent),
                  ),
                  const SizedBox(height: 22),
                  FilledButton(
                    key: const ValueKey('suspended-support'),
                    style: velaFilledButtonStyle(),
                    onPressed: _contactSupport,
                    child: const Text('Contact support'),
                  ),
                  const SizedBox(height: 10),
                  OutlinedButton(
                    key: const ValueKey('suspended-sign-in'),
                    style: velaOutlinedButtonStyle(),
                    onPressed: _signInAgain,
                    child: const Text('Sign in with another account'),
                  ),
                  const SizedBox(height: 4),
                  TextButton(
                    key: const ValueKey('suspended-guest'),
                    style: velaTextButtonStyle(),
                    onPressed: _continueAsGuest,
                    child: const Text('Continue as a guest'),
                  ),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }
}
