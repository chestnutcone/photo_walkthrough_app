import 'dart:io';

import 'package:flutter_test/flutter_test.dart';

/// Final review I-4 / I-5: the native release configuration is complete and
/// a release cannot be built with placeholder values or the debug key.
void main() {
  group('iOS entitlements', () {
    final ent = File('ios/Runner/Runner.entitlements').readAsStringSync();

    test('Sign in with Apple is entitled (Default)', () {
      expect(
        RegExp(
          r'<key>com\.apple\.developer\.applesignin</key>\s*<array>\s*'
          r'<string>Default</string>\s*</array>',
        ).hasMatch(ent),
        isTrue,
      );
    });

    test('associated domains carry the applinks entry', () {
      expect(
        ent,
        contains('<key>com.apple.developer.associated-domains</key>'),
      );
      expect(ent, matches(RegExp(r'<string>applinks:[a-z0-9.-]+</string>')));
    });
  });

  group('Android release guard', () {
    final gradle = File('android/app/build.gradle.kts').readAsStringSync();

    test('release signs with key.properties, never silently with debug', () {
      expect(gradle, contains('rootProject.file("key.properties")'));
      expect(gradle, contains('create("release")'));
      expect(gradle, contains('velaReleaseProblems'));
      expect(gradle, contains('GradleException'));
    });

    test(
      'the guard checks the placeholder domain and every release define',
      () {
        for (final s in [
          'vela.example',
          'API_BASE_URL',
          'LINKS_DOMAIN',
          'APP_STORE_URL',
          'PLAY_STORE_URL',
          'dart-defines',
        ]) {
          expect(gradle, contains(s), reason: s);
        }
      },
    );
  });
}
