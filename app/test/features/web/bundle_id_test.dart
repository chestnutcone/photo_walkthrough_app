import 'dart:io';

import 'package:flutter_test/flutter_test.dart';
import 'package:vela_app/core/branding.dart';

void main() {
  test('android_application_id_is_the_branding_bundle_id', () {
    final gradle = File('android/app/build.gradle.kts').readAsStringSync();
    expect(gradle, contains('applicationId = "${Branding.bundleId}"'));
  });

  test('ios_bundle_ids_are_the_branding_bundle_id', () {
    final pbx = File('ios/Runner.xcodeproj/project.pbxproj').readAsStringSync();
    final ids = RegExp(r'PRODUCT_BUNDLE_IDENTIFIER = ([^;]+);')
        .allMatches(pbx)
        .map((m) => m.group(1)!)
        .toList();
    expect(ids, hasLength(6));
    for (final id in ids) {
      expect(
        id == Branding.bundleId || id == '${Branding.bundleId}.RunnerTests',
        isTrue,
        reason: id,
      );
    }
  });
}
