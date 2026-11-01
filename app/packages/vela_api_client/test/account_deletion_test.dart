import 'package:test/test.dart';
import 'package:vela_api_client/vela_api_client.dart';

// tests for AccountDeletion
void main() {
  final AccountDeletion? instance = /* AccountDeletion(...) */ null;
  // TODO add properties to the entity

  group(AccountDeletion, () {
    // deferred_review = content is under moderation review; the hard delete waits for it
    // String state
    test('to test the property `state`', () async {
      // TODO
    });

    // For deferred_review: the latest moment the hard delete runs (30 days after the request); earlier once the reviews end. Null when scheduled
    // DateTime completesBy
    test('to test the property `completesBy`', () async {
      // TODO
    });

  });
}
