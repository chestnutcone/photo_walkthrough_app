import 'package:test/test.dart';
import 'package:vela_api_client/vela_api_client.dart';

// tests for CreateDraftResult
void main() {
  final CreateDraftResult? instance = /* CreateDraftResult(...) */ null;
  // TODO add properties to the entity

  group(CreateDraftResult, () {
    // String workId
    test('to test the property `workId`', () async {
      // TODO
    });

    // Write-only signed URL for the original image (PUT with x-ms-blob-type: BlockBlob), valid for at most 15 minutes
    // String uploadUrl
    test('to test the property `uploadUrl`', () async {
      // TODO
    });

    // When the upload URL stops working (the draft itself lives 24 hours)
    // DateTime expiresAt
    test('to test the property `expiresAt`', () async {
      // TODO
    });

  });
}
