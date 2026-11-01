import 'package:test/test.dart';
import 'package:vela_api_client/vela_api_client.dart';

// tests for ExportJob
void main() {
  final ExportJob? instance = /* ExportJob(...) */ null;
  // TODO add properties to the entity

  group(ExportJob, () {
    // String id
    test('to test the property `id`', () async {
      // TODO
    });

    // expired = past its 7-day life; request a new export
    // String status
    test('to test the property `status`', () async {
      // TODO
    });

    // Read-only signed URL for the zip, valid for at most 15 minutes; present only while status is ready (fetch the export again for a fresh one)
    // String downloadUrl
    test('to test the property `downloadUrl`', () async {
      // TODO
    });

    // DateTime createdAt
    test('to test the property `createdAt`', () async {
      // TODO
    });

    // DateTime expiresAt
    test('to test the property `expiresAt`', () async {
      // TODO
    });

  });
}
