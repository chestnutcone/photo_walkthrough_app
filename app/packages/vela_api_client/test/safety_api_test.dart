import 'package:test/test.dart';
import 'package:vela_api_client/vela_api_client.dart';


/// tests for SafetyApi
void main() {
  final instance = VelaApiClient().getSafetyApi();

  group(SafetyApi, () {
    // Block a user
    //
    //Future<Envelope> block(String userId) async
    test('test block', () async {
      // TODO
    });

    // Report a work or reply
    //
    // An illegal_content report hides the item at once. Any other reason hides it only once 3 distinct accounts have pending reports on it, or when the reporter's account is older than 7 days; until then it waits for review. The note is cleaned by the shared text sanitizer.
    //
    //Future<Envelope> report(ReportRequest reportRequest) async
    test('test report', () async {
      // TODO
    });

    // Unblock a user
    //
    //Future<Envelope> unblock(String userId) async
    test('test unblock', () async {
      // TODO
    });

  });
}
