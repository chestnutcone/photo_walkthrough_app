import 'dart:io';

import 'package:flutter_test/flutter_test.dart';

/// Search text can be personal data. The feature's source must have no
/// logging, telemetry or crash-breadcrumb call at all, so no query can reach
/// one by accident.
void main() {
  test('search_sources_never_log_or_report', () {
    final files = Directory('lib/features/search')
        .listSync()
        .whereType<File>()
        .where((f) => f.path.endsWith('.dart'));
    expect(files, isNotEmpty);
    final banned = RegExp(
      r'\b(print|debugPrint|log|developer\.log|debugPrintStack)\s*\(|sentry|breadcrumb|analytics|telemetry',
      caseSensitive: false,
    );
    for (final f in files) {
      // Comments may name the rule; code may not break it.
      final code = f
          .readAsLinesSync()
          .where((l) => !l.trimLeft().startsWith('//'))
          .join('\n');
      expect(banned.hasMatch(code), isFalse, reason: f.path);
    }
  });
}
