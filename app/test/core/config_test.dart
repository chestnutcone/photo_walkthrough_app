import 'package:flutter/foundation.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:vela_app/core/config.dart';

String _url(String defined, TargetPlatform p, {bool web = false}) =>
    AppConfig.resolveApiBaseUrl(defined: defined, platform: p, isWeb: web);

void main() {
  test('dart-define wins', () {
    expect(_url('https://api.x', TargetPlatform.android), 'https://api.x');
  });
  test('android emulator default', () {
    expect(_url('', TargetPlatform.android), 'http://10.0.2.2:5000');
  });
  test('localhost elsewhere', () {
    expect(_url('', TargetPlatform.iOS), 'http://localhost:5000');
    expect(
      _url('', TargetPlatform.android, web: true),
      'http://localhost:5000',
    );
  });
}
