import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_exception.dart';
import '../../core/theme/tokens.dart';
import 'auth_messages.dart';
import 'email_code_screen.dart';
import 'sign_in_service.dart';
import 'sso_buttons.dart';
import 'sso_client.dart';

/// Opens the sign-in sheet. True only when a session was stored; closing the
/// sheet any other way is false.
///
/// A new account is sent to onboarding by the router as soon as the session
/// is stored, so the screen that asked for sign-in may already be gone when
/// this returns true. Callers must check `context.mounted` before going on
/// (the follow, reply and inquiry flows do).
Future<bool> showSignInSheet(BuildContext context) async {
  final signedIn = await showModalBottomSheet<bool>(
    context: context,
    isScrollControlled: true,
    useSafeArea: true,
    enableDrag: false,
    showDragHandle: false,
    builder: (_) => const SignInSheet(),
  );
  return signedIn ?? false;
}

enum _Step { choose, email, code }

class SignInSheet extends ConsumerStatefulWidget {
  const SignInSheet({super.key});

  @override
  ConsumerState<SignInSheet> createState() => _SignInSheetState();
}

class _SignInSheetState extends ConsumerState<SignInSheet> {
  _Step _step = _Step.choose;
  bool _busy = false;
  String? _error;
  String _email = '';

  Future<void> _sso(String provider) async {
    if (_busy) return;
    setState(() {
      _busy = true;
      _error = null;
    });
    String? error;
    var done = false;
    try {
      await ref.read(signInServiceProvider).signInWithSso(provider);
      done = true;
    } on SsoCancelledException {
      // The user backed out: no message.
    } on ApiException catch (e) {
      error = signInErrorMessage(e);
    } on SsoFailureException {
      error = "We couldn't complete that sign-in. Try again.";
    }
    if (!mounted) return;
    setState(() {
      _busy = false;
      _error = error;
    });
    if (done) Navigator.of(context).pop(true);
  }

  void _signedIn() => Navigator.of(context).pop(true);

  @override
  Widget build(BuildContext context) {
    final inset = MediaQuery.of(context).viewInsets.bottom;
    return PopScope(
      canPop: !_busy,
      child: AnimatedPadding(
        duration: const Duration(milliseconds: 150),
        padding: EdgeInsets.only(bottom: inset),
        child: SingleChildScrollView(
          padding: const EdgeInsets.fromLTRB(20, 14, 20, 24),
          child: switch (_step) {
            _Step.choose => _choose(),
            _Step.email => EmailEntryStep(
              onBack: () => setState(() => _step = _Step.choose),
              onRequested: (email) => setState(() {
                _email = email;
                _step = _Step.code;
              }),
            ),
            _Step.code => EmailCodeScreen(
              email: _email,
              onSignedIn: _signedIn,
              onChangeEmail: () => setState(() => _step = _Step.email),
            ),
          },
        ),
      ),
    );
  }

  Widget _choose() => Column(
    mainAxisSize: MainAxisSize.min,
    crossAxisAlignment: CrossAxisAlignment.stretch,
    children: [
      Row(
        children: [
          Expanded(
            child: Semantics(
              header: true,
              child: Text('Sign in', style: VelaText.serif28),
            ),
          ),
          IconButton(
            key: const ValueKey('sheet-close'),
            tooltip: 'Close',
            constraints: const BoxConstraints(minWidth: 44, minHeight: 44),
            onPressed: _busy ? null : () => Navigator.of(context).pop(false),
            icon: const Icon(Icons.close, color: VelaColors.ink),
          ),
        ],
      ),
      const SizedBox(height: 4),
      Text(
        'Follow artists, reply to details and share your own stories.',
        style: VelaText.sans14.copyWith(color: VelaColors.secondary),
      ),
      const SizedBox(height: 20),
      SsoButtons(
        enabled: !_busy,
        onApple: ref.watch(appleSignInAvailableProvider)
            ? () => _sso('apple')
            : null,
        onGoogle: () => _sso('google'),
        onEmail: () => setState(() {
          _error = null;
          _step = _Step.email;
        }),
      ),
      if (_busy)
        const Padding(
          padding: EdgeInsets.only(top: 16),
          child: Center(
            child: SizedBox(
              width: 22,
              height: 22,
              child: CircularProgressIndicator(
                strokeWidth: 2,
                color: VelaColors.accent,
              ),
            ),
          ),
        ),
      if (_error != null)
        Padding(
          padding: const EdgeInsets.only(top: 14),
          child: Semantics(
            liveRegion: true,
            child: Text(
              _error!,
              key: const ValueKey('sign-in-error'),
              style: VelaText.sans13.copyWith(color: VelaColors.danger),
            ),
          ),
        ),
      const SizedBox(height: 18),
      Text(
        'Each sign-in method is its own account. Signing in with Google and '
        'with email creates two separate accounts; they are never merged.',
        style: VelaText.sans12.copyWith(color: VelaColors.muted, height: 1.45),
      ),
    ],
  );
}
