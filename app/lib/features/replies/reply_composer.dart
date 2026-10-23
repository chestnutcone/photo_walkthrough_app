import 'dart:math' as math;

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/require_sign_in.dart';
import '../../core/theme/tokens.dart';
import 'bidi.dart';
import 'replies_controller.dart';

const int _counterFrom = 900;
const Color _errorInk = Color(0xFFE59A8A);

/// Truncates to [kReplyMaxLength] code points, never splitting a surrogate
/// pair. Paste over the limit is cut, typing past it is refused.
class CodePointLimiter extends TextInputFormatter {
  const CodePointLimiter();

  @override
  TextEditingValue formatEditUpdate(
    TextEditingValue oldValue,
    TextEditingValue newValue,
  ) {
    if (codePointCount(newValue.text) <= kReplyMaxLength) return newValue;
    final cut = String.fromCharCodes(newValue.text.runes.take(kReplyMaxLength));
    return TextEditingValue(
      text: cut,
      selection: TextSelection.collapsed(
        offset: math.min(newValue.selection.end, cut.length),
      ),
    );
  }
}

final _controlChars = RegExp(r'[\u0000-\u0009\u000B-\u001F\u007F-\u009F]');

/// Composer row: pill input, Send, counter near the limit and inline error.
class ReplyComposer extends ConsumerStatefulWidget {
  const ReplyComposer({required this.keyArg, super.key});

  final RepliesKey keyArg;

  @override
  ConsumerState<ReplyComposer> createState() => _ReplyComposerState();
}

class _ReplyComposerState extends ConsumerState<ReplyComposer> {
  final _text = TextEditingController();
  bool _busy = false;

  @override
  void initState() {
    super.initState();
    _text.addListener(() => setState(() {}));
  }

  @override
  void dispose() {
    _text.dispose();
    super.dispose();
  }

  bool get _canSend => !_busy && _text.text.trim().isNotEmpty;

  Future<void> _send() async {
    if (!_canSend) return;
    final text = _text.text;
    setState(() => _busy = true);
    try {
      // A guest keeps the typed text if sign-in is cancelled.
      if (!await requireSignIn(context, ref) || !mounted) return;
      final outcome = await ref
          .read(repliesControllerProvider(widget.keyArg).notifier)
          .send(text);
      if (outcome == SendOutcome.sent && mounted && _text.text == text) {
        _text.clear();
      }
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final state = ref.watch(repliesControllerProvider(widget.keyArg));
    final count = codePointCount(_text.text);
    final error = state.sendError;
    final sending = _busy || state.sending;
    return Column(
      mainAxisSize: MainAxisSize.min,
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        if (error != null)
          Padding(
            padding: const EdgeInsets.only(bottom: 6),
            child: Semantics(
              liveRegion: true,
              child: Text(
                describeReplyError(error),
                style: VelaText.sans12.copyWith(color: _errorInk),
              ),
            ),
          ),
        Row(
          crossAxisAlignment: CrossAxisAlignment.end,
          children: [
            Expanded(
              child: TextField(
                key: const ValueKey('composer-input'),
                controller: _text,
                minLines: 1,
                maxLines: 4,
                textDirection: firstStrongDirection(_text.text),
                keyboardType: TextInputType.multiline,
                textCapitalization: TextCapitalization.sentences,
                style: VelaText.sans14,
                cursorColor: VelaColors.accent,
                inputFormatters: [
                  FilteringTextInputFormatter.deny(_controlChars),
                  const CodePointLimiter(),
                ],
                decoration: InputDecoration(
                  hintText: 'Reply to this detail…',
                  hintStyle: VelaText.sans14.copyWith(color: VelaColors.faint),
                  filled: true,
                  fillColor: VelaColors.input,
                  isDense: true,
                  contentPadding: const EdgeInsets.symmetric(
                    horizontal: 16,
                    vertical: 11,
                  ),
                  border: OutlineInputBorder(
                    borderRadius: BorderRadius.circular(22),
                    borderSide: BorderSide.none,
                  ),
                ),
              ),
            ),
            const SizedBox(width: 8),
            FilledButton(
              key: const ValueKey('composer-send'),
              onPressed: _canSend ? _send : null,
              style: FilledButton.styleFrom(
                backgroundColor: VelaColors.ink,
                foregroundColor: VelaColors.background,
                disabledBackgroundColor: VelaColors.hairline,
                disabledForegroundColor: VelaColors.faint,
                shape: const StadiumBorder(),
                padding: const EdgeInsets.symmetric(horizontal: 16),
                minimumSize: const Size(64, 44),
              ),
              child: Semantics(
                label: 'Send reply',
                excludeSemantics: true,
                child: Text(sending ? '…' : 'Send', style: VelaText.sans13s),
              ),
            ),
          ],
        ),
        if (count >= _counterFrom)
          Padding(
            padding: const EdgeInsets.only(top: 4, right: 4),
            child: Align(
              alignment: AlignmentDirectional.centerEnd,
              child: Text(
                '$count / $kReplyMaxLength',
                style: VelaText.mono10.copyWith(
                  color: count >= kReplyMaxLength
                      ? VelaColors.accent
                      : VelaColors.faint,
                ),
              ),
            ),
          ),
      ],
    );
  }
}
