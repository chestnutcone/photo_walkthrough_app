import 'package:test/test.dart';
import 'package:vela_api_client/vela_api_client.dart';


/// tests for WorksApi
void main() {
  final instance = VelaApiClient().getWorksApi();

  group(WorksApi, () {
    // Create a draft and get an upload URL
    //
    // Counts against the tier's story limit. Errors: 409 quota_exceeded (message includes the limit) when the account is at its limit; 409 onboarding_required before onboarding.
    //
    //Future<CreateDraftResultEnvelope> createDraft(CreateDraftRequest createDraftRequest, { String xAttestation, String xPlatform }) async
    test('test createDraft', () async {
      // TODO
    });

    // Delete a work
    //
    //Future<Envelope> deleteWork(String id) async
    test('test deleteWork', () async {
      // TODO
    });

    // Following or discover feed
    //
    // Public. `discover` is newest first. `following` lists works by followed artists first, then everyone else's, each newest first; a guest, or a viewer who follows nobody, gets `discover`. Invalid cursor: 400 invalid_cursor.
    //
    //Future<WorkListEnvelope> getFeed(String tab, { String cursor, int limit }) async
    test('test getFeed', () async {
      // TODO
    });

    // Get a work
    //
    //Future<WorkEnvelope> getWork(String id) async
    test('test getWork', () async {
      // TODO
    });

    // Publish a draft
    //
    //Future<WorkEnvelope> publishWork(String id) async
    test('test publishWork', () async {
      // TODO
    });

    // Update a work's title and chapters
    //
    //Future<WorkEnvelope> updateWork(String id, UpdateWorkRequest updateWorkRequest) async
    test('test updateWork', () async {
      // TODO
    });

  });
}
