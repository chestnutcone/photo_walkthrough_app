import 'package:unorm_dart/unorm_dart.dart' as unorm;

/// The app's copy of the API's `Common/TextSanitizer.cs` (S2-2). Both run the
/// shared vectors in `contract/text-sanitizer-vectors.json`, so a length shown
/// while typing is the length the server checks. Change both or neither.
///
/// Steps: drop lone surrogates, NFC, drop control characters, map every
/// Unicode space (and new lines, unless [multiline]) to a space, drop
/// invisible format characters except the few that carry meaning, cap
/// stacked combining marks at [maxStackedMarks], collapse spaces and trim.
const int maxStackedMarks = 4;

final _format = RegExp(r'^\p{Cf}$', unicode: true);
final _control = RegExp(r'^\p{Cc}$', unicode: true);
final _stackingMark = RegExp(r'^[\p{Mn}\p{Me}]$', unicode: true);
final _letterOrMark = RegExp(r'^[\p{L}\p{M}]$', unicode: true);

/// Cleans [raw] the way the server does before it stores or counts it.
String cleanUserText(String? raw, {bool multiline = false}) {
  if (raw == null || raw.isEmpty) return '';
  final kept = _filter(_decode(raw), multiline);
  return multiline ? _collapseLines(kept) : _collapseSpaces(kept).trim();
}

/// The length the server checks: code points of the cleaned text.
int userTextLength(String raw, {bool multiline = false}) =>
    cleanUserText(raw, multiline: multiline).runes.length;

List<int> _decode(String raw) {
  final valid = String.fromCharCodes(
    raw.runes.where((r) => r < 0xD800 || r > 0xDFFF),
  );
  final normal = unorm
      .nfc(valid)
      .replaceAll('\r\n', '\n')
      .replaceAll('\r', '\n');
  return normal.runes.toList(growable: false);
}

List<int> _filter(List<int> cps, bool multiline) {
  final out = <int>[];
  var marks = 0;
  for (var i = 0; i < cps.length; i++) {
    final prev = out.isEmpty ? -1 : out.last;
    final next = i + 1 < cps.length ? cps[i + 1] : -1;
    final mapped = _map(cps[i], prev, next, multiline);
    if (mapped < 0) continue;
    if (_is(_stackingMark, mapped)) {
      if (++marks > maxStackedMarks) continue;
    } else {
      marks = 0;
    }
    out.add(mapped);
  }
  return out;
}

int _map(int cp, int prev, int next, bool multiline) {
  if (cp == 0x0A) return multiline ? 0x0A : 0x20;
  if (_isSpace(cp)) return 0x20;
  if (_is(_control, cp)) return -1;
  if (cp == 0x115F || cp == 0x1160 || cp == 0x3164 || cp == 0xFFA0) return -1;
  if (cp == 0x200D) {
    final emoji = _isEmojiPart(prev) && _isEmoji(next);
    return emoji || (_isJoinable(prev) && _isJoinable(next)) ? cp : -1;
  }
  if (cp == 0x200C) return _isJoinable(prev) && _isJoinable(next) ? cp : -1;
  if (cp >= 0xE0020 && cp <= 0xE007F) {
    return prev == 0x1F3F4 || (prev >= 0xE0020 && prev <= 0xE007E) ? cp : -1;
  }
  if (_is(_format, cp)) return _keepFormat(cp) ? cp : -1;
  return cp;
}

bool _is(RegExp category, int cp) =>
    cp >= 0 && category.hasMatch(String.fromCharCode(cp));

bool _keepFormat(int cp) =>
    cp == 0x200E ||
    cp == 0x200F ||
    cp == 0x061C ||
    (cp >= 0x0600 && cp <= 0x0605) ||
    cp == 0x06DD ||
    cp == 0x070F ||
    cp == 0x0890 ||
    cp == 0x0891 ||
    cp == 0x08E2 ||
    cp == 0x110BD ||
    cp == 0x110CD;

bool _isSpace(int cp) =>
    (cp >= 0x09 && cp <= 0x0D) ||
    cp == 0x20 ||
    cp == 0x85 ||
    cp == 0xA0 ||
    cp == 0x1680 ||
    (cp >= 0x2000 && cp <= 0x200A) ||
    cp == 0x2028 ||
    cp == 0x2029 ||
    cp == 0x202F ||
    cp == 0x205F ||
    cp == 0x3000;

bool _isJoinable(int cp) => cp >= 0x0590 && _is(_letterOrMark, cp);

bool _isEmoji(int cp) =>
    (cp >= 0x1F000 && cp <= 0x1FAFF) ||
    (cp >= 0x2600 && cp <= 0x27BF) ||
    (cp >= 0x2300 && cp <= 0x23FF) ||
    (cp >= 0x2B00 && cp <= 0x2BFF) ||
    (cp >= 0x2194 && cp <= 0x21AA) ||
    const {
      0x00A9,
      0x00AE,
      0x203C,
      0x2049,
      0x2122,
      0x2139,
      0x3030,
      0x303D,
      0x3297,
      0x3299,
    }.contains(cp);

bool _isEmojiPart(int cp) => _isEmoji(cp) || cp == 0xFE0F || cp == 0x20E3;

String _collapseSpaces(List<int> cps) {
  final sb = StringBuffer();
  var lastSpace = false;
  for (final cp in cps) {
    if (cp == 0x20 && lastSpace) continue;
    lastSpace = cp == 0x20;
    sb.writeCharCode(cp);
  }
  return sb.toString();
}

String _collapseLines(List<int> cps) {
  final sb = StringBuffer();
  var blanks = 0;
  for (final raw in _collapseSpaces(cps).split('\n')) {
    final line = raw.trim();
    if (line.isEmpty) {
      blanks++;
      continue;
    }
    if (sb.isNotEmpty) sb.write(blanks > 0 ? '\n\n' : '\n');
    sb.write(line);
    blanks = 0;
  }
  return sb.toString();
}
