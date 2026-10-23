import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_exception.dart';
import '../../core/theme/form_styles.dart';
import '../../core/theme/tokens.dart';
import 'auth_messages.dart';
import 'code_boxes.dart';
import 'sign_in_service.dart';

/// Step 1 of the email flow: ask for the address and request a code.
class EmailEntryStep extends ConsumerStatefulWidget {
  const EmailEntryStep({
    super.key,
    required this.onRequested,
    required this.onBack,
  });

  final ValueChanged<String> onRequested;
  final VoidCallback onBack;

  @override
  ConsumerState<EmailEntryStep> createState() => _EmailEntryStepState();
}

class _EmailEntryStepState extends ConsumerState<EmailEntryStep> {
  final _controller = TextEditingController();
  bool _busy = false;
  String? _error;

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  Future<void> _send() async {
    if (_busy || !looksLikeEmail(_controller.text)) return;
    final email = normalizeEmail(_controller.text);
    setState(() {
      _busy = true;
      _error = null;
    });
    String? error;
    try {
      await ref.read(signInServiceProvider).requestEmailCode(email);
    } on ApiException catch (e) {
      error = signInErrorMessage(e);
    }
    if (!mounted) return;
    setState(() {
      _busy = false;
      _error = error;
    });
    if (error == null) widget.onRequested(email);
  }

  @override
  Widget build(BuildContext context) => Column(
    mainAxisSize: MainAxisSize.min,
    crossAxisAlignment: CrossAxisAlignment.stretch,
    children: [
      _StepHeader(title: 'Your email', onBack: widget.onBack),
      const SizedBox(height: 4),
      Text(
        "We'll email you a 6-digit code. No password needed.",
        style: VelaText.sans14.copyWith(color: VelaColors.secondary),
      ),
      const SizedBox(height: 16),
      ListenableBuilder(
        listenable: _controller,
        builder: (context, _) => Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            TextField(
              key: const ValueKey('email-field'),
              controller: _controller,
              autofocus: true,
              enabled: !_busy,
              keyboardType: TextInputType.emailAddress,
              textInputAction: TextInputAction.send,
              autofillHints: const [AutofillHints.email],
              autocorrect: false,
              enableSuggestions: false,
              inputFormatters: [LengthLimitingTextInputFormatter(254)],
              style: VelaText.sans15,
              decoration: velaInputDecoration(
                hint: 'you@example.com',
                errorText: _error,
              ),
              onSubmitted: (_) => _send(),
            ),
            const SizedBox(height: 14),
            FilledButton(
              key: const ValueKey('email-send'),
              style: velaFilledButtonStyle(),
              onPressed: !_busy && looksLikeEmail(_controller.text)
                  ? _send
                  : null,
              child: Text(_busy ? 'Sending…' : 'Send code'),
            ),
          ],
        ),
      ),
    ],
  );
}

/// Step 2: enter the 6-digit code.
///
/// The server allows five guesses per code and refuses a sixth even when it
/// is right, but never says how many are left, so the app counts its own
/// guesses for this code and asks for a new code after five.
class EmailCodeScreen extends ConsumerStatefulWidget {
  const EmailCodeScreen({
    super.key,
    required this.email,
    required this.onSignedIn,
    required this.onChangeEmail,
  });

  final String email;
  final VoidCallback onSignedIn;
  final VoidCallback onChangeEmail;

  @override
  ConsumerState<EmailCodeScreen> createState() => _EmailCodeScreenState();
}

class _EmailCodeScreenState extends ConsumerState<EmailCodeScreen> {
  final _controller = TextEditingController();
  Timer? _timer;
  int _cooldown = defaultResendCooldownSeconds;
  int _attemptsUsed = 0;
  bool _busy = false;
  String? _error;
  String? _notice;

  bool get _exhausted => _attemptsUsed >= maxCodeAttempts;

  @override
  void initState() {
    super.initState();
    _startCooldown(defaultResendCooldownSeconds);
  }

  @override
  void dispose() {
    _timer?.cancel();
    _controller.dispose();
    super.dispose();
  }

  void _startCooldown(int seconds) {
    _timer?.cancel();
    _cooldown = seconds;
    if (seconds <= 0) return;
    _timer = Timer.periodic(const Duration(seconds: 1), (t) {
      if (!mounted) return t.cancel();
      setState(() => _cooldown--);
      if (_cooldown <= 0) t.cancel();
    });
  }

  Future<void> _submit() async {
    final code = _controller.text;
    if (_busy || _exhausted || code.length != codeLength) return;
    setState(() {
      _busy = true;
      _error = null;
      _notice = null;
    });
    String? error;
    var done = false;
    try {
      await ref.read(signInServiceProvider).verifyEmailCode(widget.email, code);
      done = true;
    } on ApiException catch (e) {
      error = _verifyError(e);
    }
    if (!mounted) return;
    if (done) return widget.onSignedIn();
    _controller.clear();
    setState(() {
      _busy = false;
      _error = error;
    });
  }

  /// A wrong or expired code costs one of the five guesses; a rate limit or
  /// network failure does not.
  String _verifyError(ApiException e) {
    if (e.code != 'invalid_code') return signInErrorMessage(e);
    _attemptsUsed++;
    if (_exhausted) {
      return 'Too many wrong codes. Request a new code to try again.';
    }
    final left = maxCodeAttempts - _attemptsUsed;
    return "That code isn't right, or it has expired. "
        '$left ${left == 1 ? 'attempt' : 'attempts'} left.';
  }

  Future<void> _resend() async {
    if (_busy) return;
    setState(() {
      _busy = true;
      _error = null;
      _notice = null;
    });
    String? error;
    int? wait;
    try {
      await ref.read(signInServiceProvider).requestEmailCode(widget.email);
    } on ApiException catch (e) {
      error = signInErrorMessage(e);
      wait = e.retryAfter?.inSeconds;
    }
    if (!mounted) return;
    _controller.clear();
    setState(() {
      _busy = false;
      _error = error;
      if (error == null) {
        _attemptsUsed = 0;
        _notice = 'A new code is on its way.';
      }
    });
    _startCooldown(error == null ? defaultResendCooldownSeconds : (wait ?? 30));
  }

  @override
  Widget build(BuildContext context) => Column(
    mainAxisSize: MainAxisSize.min,
    crossAxisAlignment: CrossAxisAlignment.stretch,
    children: [
      _StepHeader(title: 'Enter your code', onBack: widget.onChangeEmail),
      const SizedBox(height: 4),
      Text(
        'If that address can receive mail, a 6-digit code is on its way to '
        '${widget.email}. It expires in 10 minutes.',
        style: VelaText.sans14.copyWith(color: VelaColors.secondary),
      ),
      const SizedBox(height: 18),
      if (!_exhausted)
        CodeBoxes(
          controller: _controller,
          enabled: !_busy,
          onChanged: (_) => _submit(),
        ),
      if (_error != null)
        Padding(
          padding: const EdgeInsets.only(top: 12),
          child: Semantics(
            liveRegion: true,
            child: Text(
              _error!,
              key: const ValueKey('code-error'),
              style: VelaText.sans13.copyWith(color: VelaColors.danger),
            ),
          ),
        ),
      if (_notice != null)
        Padding(
          padding: const EdgeInsets.only(top: 12),
          child: Semantics(
            liveRegion: true,
            child: Text(_notice!, style: VelaText.sans13),
          ),
        ),
      const SizedBox(height: 14),
      if (_exhausted)
        FilledButton(
          key: const ValueKey('code-new'),
          style: velaFilledButtonStyle(),
          onPressed: _busy ? null : _resend,
          child: const Text('Send a new code'),
        )
      else ...[
        ListenableBuilder(
          listenable: _controller,
          builder: (context, _) => FilledButton(
            key: const ValueKey('code-submit'),
            style: velaFilledButtonStyle(),
            onPressed: !_busy && _controller.text.length == codeLength
                ? _submit
                : null,
            child: Text(_busy ? 'Checking…' : 'Continue'),
          ),
        ),
        TextButton(
          key: const ValueKey('code-resend'),
          style: velaTextButtonStyle(),
          onPressed: _busy || _cooldown > 0 ? null : _resend,
          child: Text(
            _cooldown > 0 ? 'Resend code in ${_cooldown}s' : 'Resend code',
          ),
        ),
      ],
      TextButton(
        key: const ValueKey('code-change-email'),
        style: velaTextButtonStyle(),
        onPressed: _busy ? null : widget.onChangeEmail,
        child: const Text('Use a different email'),
      ),
    ],
  );
}

class _StepHeader extends StatelessWidget {
  const _StepHeader({required this.title, required this.onBack});

  final String title;
  final VoidCallback onBack;

  @override
  Widget build(BuildContext context) => Row(
    children: [
      IconButton(
        tooltip: 'Back',
        constraints: const BoxConstraints(minWidth: 44, minHeight: 44),
        onPressed: onBack,
        icon: const Icon(Icons.arrow_back, color: VelaColors.ink),
      ),
      Expanded(
        child: Semantics(
          header: true,
          child: Text(title, style: VelaText.serif28),
        ),
      ),
    ],
  );
}
