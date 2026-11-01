import 'package:flutter/widgets.dart';

final RegExp _letter = RegExp(r'\p{L}', unicode: true);

bool _isRtl(int r) =>
    (r >= 0x0590 && r <= 0x08FF) ||
    (r >= 0xFB1D && r <= 0xFDFF) ||
    (r >= 0xFE70 && r <= 0xFEFF) ||
    (r >= 0x10800 && r <= 0x10FFF) ||
    (r >= 0x1E800 && r <= 0x1EFFF);

/// Direction of the first strong character of [text] (the Unicode "first
/// strong" rule used for auto direction), or null when there is none, e.g.
/// emoji-only or digits-only text. Flutter has no automatic direction, so each
/// reply picks its own.
TextDirection? firstStrongDirection(String text) {
  for (final r in text.runes) {
    // Only letters are strong; digits and punctuation are neutral.
    if (!_letter.hasMatch(String.fromCharCode(r))) continue;
    return _isRtl(r) ? TextDirection.rtl : TextDirection.ltr;
  }
  return null;
}

/// [firstStrongDirection], falling back to the ambient direction.
TextDirection textDirectionFor(BuildContext context, String text) =>
    firstStrongDirection(text) ?? Directionality.of(context);
