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
