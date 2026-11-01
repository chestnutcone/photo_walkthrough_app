import 'package:flutter/services.dart';

import 'text_sanitizer.dart';

/// Caps a field at [max] characters counted exactly as the server counts
/// them: code points of the cleaned text ([userTextLength]). Invisible
/// characters the server strips do not use up the limit, and an emoji is one
/// character. A paste that is too long is cut to the longest prefix that fits;
/// a surrogate pair is never split.
class UserTextFormatter extends TextInputFormatter {
  UserTextFormatter(this.max, {this.multiline = false});

  final int max;
  final bool multiline;

  @override
  TextEditingValue formatEditUpdate(
    TextEditingValue oldValue,
    TextEditingValue newValue,
  ) {
    if (_fits(newValue.text)) return newValue;
    final runes = newValue.text.runes.toList(growable: false);
    var lo = 0, hi = runes.length;
    while (lo < hi) {
      final mid = (lo + hi + 1) ~/ 2;
      if (_fits(String.fromCharCodes(runes.take(mid)))) {
        lo = mid;
      } else {
        hi = mid - 1;
      }
    }
    final cut = String.fromCharCodes(runes.take(lo));
    return TextEditingValue(
      text: cut,
      selection: TextSelection.collapsed(offset: cut.length),
    );
  }

  bool _fits(String text) => userTextLength(text, multiline: multiline) <= max;
}
