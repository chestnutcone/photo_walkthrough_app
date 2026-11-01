import 'package:test/test.dart';
import 'package:vela_api_client/vela_api_client.dart';


/// tests for SearchApi
void main() {
  final instance = VelaApiClient().getSearchApi();

  group(SearchApi, () {
    // Search artists or stories
    //
    // Public. `q` is 2 to 80 characters after trimming (shorter: 400 query_too_short; longer or containing a null character: 400 validation_failed); whitespace-separated terms (first 5) must all match, with typo tolerance. `type` defaults to `artists`; `data` fills only the list for the searched type and the other is empty. Results are ranked best match first and paged with an opaque cursor (invalid: 400 invalid_cursor). Hidden, blocked, suspended and unpublished content never appears.
    //
    //Future<SearchResultsListEnvelope> search(String q, { String type, String cursor, int limit }) async
    test('test search', () async {
      // TODO
    });

  });
}
