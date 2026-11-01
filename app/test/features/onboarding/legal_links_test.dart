import 'package:flutter_test/flutter_test.dart';
import 'package:vela_app/features/onboarding/legal_links.dart';

void main() {
  test('only_https_or_local_http_is_launchable', () {
    bool ok(String u, {bool debug = true}) =>
        isLaunchableLegalUri(Uri.parse(u), isDebug: debug);
    expect(ok('https://api.example.com/legal/terms'), isTrue);
    expect(ok('http://localhost:5080/legal/terms'), isTrue);
    expect(ok('http://10.0.2.2:5080/legal/terms'), isTrue);
    expect(ok('http://localhost:5080/legal/terms', debug: false), isFalse);
    expect(ok('http://example.com/legal/terms'), isFalse);
    expect(ok('javascript:alert(1)'), isFalse);
    expect(ok('file:///etc/passwd'), isFalse);
    expect(ok('https:///legal'), isFalse);
  });
}
