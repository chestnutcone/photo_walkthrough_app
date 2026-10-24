import 'package:flutter/foundation.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:vela_app/core/config.dart';

String _url(String defined, TargetPlatform p, {bool web = false}) =>
    AppConfig.resolveApiBaseUrl(
      defined: defined,
      platform: p,
      isWeb: web,
      isRelease: false,
    );

void main() {
  test('dart-define wins', () {
    expect(_url('https://api.x', TargetPlatform.android), 'https://api.x');
  });
  test('android emulator default', () {
    expect(_url('', TargetPlatform.android), 'http://10.0.2.2:5080');
  });
  test('localhost elsewhere', () {
    expect(_url('', TargetPlatform.iOS), 'http://localhost:5080');
    expect(
      _url('', TargetPlatform.android, web: true),
      'http://localhost:5080',
    );
  });
  test('release build without API_BASE_URL fails loudly', () {
    expect(
      () => AppConfig.resolveApiBaseUrl(
        defined: '',
        platform: TargetPlatform.android,
        isWeb: false,
        isRelease: true,
      ),
      throwsStateError,
    );
  });
  test('release_builds_refuse_a_non_https_api_url', () {
    for (final url in ['http://api.example.com', 'ftp://x', 'api.example.com', 'HTTP://API.EXAMPLE.COM']) {
      expect(
        () => AppConfig.resolveApiBaseUrl(
          defined: url,
          platform: TargetPlatform.android,
          isWeb: false,
          isRelease: true,
        ),
        throwsStateError,
        reason: url,
      );
    }
    expect(
      AppConfig.resolveApiBaseUrl(
        defined: 'https://api.example.com',
        platform: TargetPlatform.iOS,
        isWeb: false,
        isRelease: true,
      ),
      'https://api.example.com',
    );
  });
  test('debug_builds_still_accept_http_for_local_development', () {
    expect(
      AppConfig.resolveApiBaseUrl(
        defined: 'http://192.168.1.5:5080',
        platform: TargetPlatform.android,
        isWeb: false,
        isRelease: false,
      ),
      'http://192.168.1.5:5080',
    );
  });
  test('platform header', () {
    expect(AppConfig.resolvePlatformHeader(TargetPlatform.iOS, false), 'ios');
    expect(
      AppConfig.resolvePlatformHeader(TargetPlatform.android, false),
      'android',
    );
    expect(
      AppConfig.resolvePlatformHeader(TargetPlatform.android, true),
      'web',
    );
  });
}
