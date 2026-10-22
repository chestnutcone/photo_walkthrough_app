import 'package:test/test.dart';
import 'package:vela_api_client/vela_api_client.dart';


/// tests for SearchApi
void main() {
  final instance = VelaApiClient().getSearchApi();

  group(SearchApi, () {
    // Search artists or stories
    //
    //Future<SearchResultsListEnvelope> search(String q, { String type, String cursor, int limit }) async
    test('test search', () async {
      // TODO
    });

  });
}
