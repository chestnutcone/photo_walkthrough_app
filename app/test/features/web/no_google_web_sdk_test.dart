import 'dart:io';

import 'package:flutter_test/flutter_test.dart';

/// Final review I-2: the web build (the anonymous story viewer) must not
/// register the real google_sign_in_web, which injects Google's GSI script.
/// The built output is checked by scripts/check-web-viewer-origins.sh.
void main() {
  test('google_sign_in_web resolves to the local no-op package', () {
    final lock = File('pubspec.lock').readAsStringSync();
    final entry = RegExp(
      r'\n  google_sign_in_web:\n(?:    .*\n|      .*\n)+',
    ).firstMatch(lock)?.group(0);
    expect(entry, isNotNull);
    expect(entry, contains('source: path'));
    expect(entry, contains('google_sign_in_web_none'));
  });

  test('the no-op package registers nothing and loads no script', () {
    final src = File(
      'packages/google_sign_in_web_none/lib/google_sign_in_web.dart',
    ).readAsStringSync();
    expect(src, isNot(contains('http')));
    expect(src, contains('static void registerWith(Registrar registrar) {}'));
  });
}
