import 'package:flutter/services.dart';

/// Caps text at [max] UTF-16 code units, which is how the server counts
/// (`string.Length`). Flutter's own maxLength counts graphemes, so an emoji
/// would count as 1 here but 2 (or more) there. A surrogate pair is never
/// split.
class Utf16LengthFormatter extends TextInputFormatter {
  Utf16LengthFormatter(this.max);

  final int max;

  @override
  TextEditingValue formatEditUpdate(
    TextEditingValue oldValue,
    TextEditingValue newValue,
  ) {
    final text = newValue.text;
    if (text.length <= max) return newValue;
    var end = max;
    final unit = text.codeUnitAt(end - 1);
    if (unit >= 0xD800 && unit <= 0xDBFF) end--; // lone high surrogate
    final cut = text.substring(0, end);
    return TextEditingValue(
      text: cut,
      selection: TextSelection.collapsed(offset: cut.length),
    );
  }
}
