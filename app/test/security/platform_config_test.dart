import 'dart:io';

import 'package:flutter_test/flutter_test.dart';

/// Release-build security settings that no Dart code can check: they live in the Android manifest and iOS Info.plist.
void main() {
  final manifest = File('android/app/src/main/AndroidManifest.xml').readAsStringSync();
  final plist = File('ios/Runner/Info.plist').readAsStringSync();

  group('android manifest', () {
    test('requests_the_internet_permission_in_the_main_manifest', () {
      // Flutter's template adds INTERNET only to the debug and profile manifests; a release build without it cannot reach the API.
      expect(manifest, contains('android.permission.INTERNET'));
    });

    test('opts_out_of_cloud_backup_so_local_data_is_not_copied_off_device', () {
      expect(manifest, contains('android:allowBackup="false"'));
    });

    test('forbids_cleartext_http', () {
      expect(manifest, contains('android:usesCleartextTraffic="false"'));
    });

    test('exports_only_the_launcher_activity_and_is_not_debuggable', () {
      expect(RegExp('android:exported="true"').allMatches(manifest).length, 1);
      expect(manifest, isNot(contains('android:debuggable')));
    });

    test('asks_for_no_permissions_beyond_the_network', () {
      final permissions = RegExp(r'<uses-permission android:name="([^"]+)"')
          .allMatches(manifest)
          .map((m) => m.group(1)!)
          .toList();
      expect(permissions, ['android.permission.INTERNET']);
    });
  });

  group('ios info.plist', () {
    test('does_not_weaken_app_transport_security', () {
      expect(plist, isNot(contains('NSAllowsArbitraryLoads')));
      expect(plist, isNot(contains('NSExceptionAllowsInsecureHTTPLoads')));
    });

    test('does_not_expose_the_documents_folder_to_file_sharing', () {
      expect(plist, isNot(contains('UIFileSharingEnabled')));
      expect(plist, isNot(contains('LSSupportsOpeningDocumentsInPlace')));
    });

    test('declares_purpose_strings_only_for_camera_and_photos', () {
      final purposes = RegExp(r'<key>(NS\w+UsageDescription)</key>')
          .allMatches(plist)
          .map((m) => m.group(1)!)
          .toSet();
      expect(purposes, {'NSCameraUsageDescription', 'NSPhotoLibraryUsageDescription'});
    });
  });
}
