import 'dart:convert';
import 'dart:io';

import 'package:flutter_test/flutter_test.dart';
import 'package:vela_app/core/text/text_sanitizer.dart';
import 'package:vela_app/core/text/user_text_formatter.dart';

/// Shared with the API's TextSanitizerTests: the same inputs must clean and
/// count the same on both sides (S2-2).
Map<String, Object?> _vectors() {
  final file = File('../contract/text-sanitizer-vectors.json');
  return jsonDecode(file.readAsStringSync()) as Map<String, Object?>;
}

void main() {
  final doc = _vectors();

  test('mark cap matches the shared vectors', () {
    expect(maxStackedMarks, doc['maxStackedMarks']);
  });

  group('shared vectors', () {
    for (final v in (doc['vectors']! as List).cast<Map<String, Object?>>()) {
      test(v['name']! as String, () {
        final multiline = v['multiline']! as bool;
        final cleaned = cleanUserText(
          v['input'] as String,
          multiline: multiline,
        );
        expect(cleaned, v['expected']);
        expect(
          userTextLength(v['input'] as String, multiline: multiline),
          v['length'],
        );
        expect(cleanUserText(cleaned, multiline: multiline), cleaned);
      });
    }
  });

  test('lone surrogates are dropped', () {
    expect(cleanUserText('a\uD800b'), 'ab');
  });

  test('hasVisibleText needs a letter, number, symbol or punctuation', () {
    expect(hasVisibleText('Ada'), isTrue);
    expect(hasVisibleText('7'), isTrue);
    expect(hasVisibleText('\u2764'), isTrue);
    expect(hasVisibleText('!'), isTrue);
    expect(hasVisibleText(''), isFalse);
    expect(hasVisibleText(' '), isFalse);
    expect(hasVisibleText('\u0301'), isFalse);
    expect(hasVisibleText('\u200e'), isFalse);
  });

  test('null and empty are empty', () {
    expect(cleanUserText(null), '');
    expect(cleanUserText(''), '');
  });

  group('UserTextFormatter', () {
    TextEditingValue type(UserTextFormatter f, String old, String next) =>
        f.formatEditUpdate(
          TextEditingValue(text: old),
          TextEditingValue(text: next),
        );

    test('accepts text within the cleaned limit', () {
      final f = UserTextFormatter(5);
      expect(type(f, '', 'abcde').text, 'abcde');
    });

    test('invisible characters do not use up the limit', () {
      final f = UserTextFormatter(3);
      expect(type(f, '', 'a​b​c').text, 'a​b​c');
    });

    test('an emoji counts as one, as on the server', () {
      final f = UserTextFormatter(2);
      expect(type(f, '', '\u{1F3A8}\u{1F44D}').text, '\u{1F3A8}\u{1F44D}');
    });

    test('a long paste is cut to the limit without splitting a pair', () {
      final f = UserTextFormatter(3);
      final out = type(f, '', '\u{1F3A8}\u{1F3A8}\u{1F3A8}\u{1F3A8}x');
      expect(out.text, '\u{1F3A8}\u{1F3A8}\u{1F3A8}');
      expect(out.selection.baseOffset, out.text.length);
    });

    test('zalgo beyond the mark cap does not count', () {
      final f = UserTextFormatter(2);
      final zalgo = 'q${'̶' * 20}';
      expect(userTextLength(zalgo), 1 + maxStackedMarks);
      expect(type(f, '', 'ab').text, 'ab');
    });
  });
}
