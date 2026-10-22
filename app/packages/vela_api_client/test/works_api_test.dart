import 'package:test/test.dart';
import 'package:vela_api_client/vela_api_client.dart';


/// tests for WorksApi
void main() {
  final instance = VelaApiClient().getWorksApi();

  group(WorksApi, () {
    // Create a draft and get an upload URL
    //
    //Future<CreateDraftResultEnvelope> createDraft(CreateDraftRequest createDraftRequest) async
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
