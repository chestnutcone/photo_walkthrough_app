import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../../core/theme/tokens.dart';

const int codeLength = 6;

/// Six boxes drawn from one text field. One real field keeps paste, SMS and
/// mail autofill (`oneTimeCode`) and screen readers working; the digits
/// "advance" because the boxes just show the next character of its text.
class CodeBoxes extends StatelessWidget {
  const CodeBoxes({
    super.key,
    required this.controller,
    required this.onChanged,
    this.enabled = true,
  });

  final TextEditingController controller;
  final ValueChanged<String> onChanged;
  final bool enabled;

  @override
  Widget build(BuildContext context) => Semantics(
    label: '$codeLength-digit code',
    textField: true,
    child: SizedBox(
      height: 56,
      child: Stack(
        children: [
          ExcludeSemantics(
            child: ListenableBuilder(
              listenable: controller,
              builder: (context, _) => Row(
                children: [
                  for (var i = 0; i < codeLength; i++) ...[
                    if (i > 0) const SizedBox(width: 8),
                    Expanded(child: _Box(controller.text, i)),
                  ],
                ],
              ),
            ),
          ),
          Positioned.fill(
            child: TextField(
              key: const ValueKey('code-field'),
              controller: controller,
              enabled: enabled,
              autofocus: true,
              keyboardType: TextInputType.number,
              autofillHints: const [AutofillHints.oneTimeCode],
              autocorrect: false,
              enableSuggestions: false,
              showCursor: false,
              inputFormatters: [
                FilteringTextInputFormatter.digitsOnly,
                LengthLimitingTextInputFormatter(codeLength),
              ],
              style: const TextStyle(color: Colors.transparent, fontSize: 1),
              cursorColor: Colors.transparent,
              decoration: const InputDecoration(
                border: InputBorder.none,
                enabledBorder: InputBorder.none,
                focusedBorder: InputBorder.none,
                disabledBorder: InputBorder.none,
                filled: false,
                contentPadding: EdgeInsets.zero,
              ),
              onChanged: onChanged,
            ),
          ),
        ],
      ),
    ),
  );
}

class _Box extends StatelessWidget {
  const _Box(this.text, this.index);

  final String text;
  final int index;

  @override
  Widget build(BuildContext context) {
    final filled = index < text.length;
    final active = index == text.length;
    return Container(
      alignment: Alignment.center,
      decoration: BoxDecoration(
        color: VelaColors.input,
        borderRadius: BorderRadius.circular(10),
        border: Border.all(
          color: active ? VelaColors.accent : Colors.transparent,
          width: 1.5,
        ),
      ),
      child: Text(
        filled ? text[index] : '',
        style: VelaText.serif28,
      ),
    );
  }
}
