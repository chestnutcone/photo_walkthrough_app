import 'package:flutter/foundation.dart';

/// Build-time configuration. Release builds must pass
/// `--dart-define=API_BASE_URL=https://api.example.com`.
abstract final class AppConfig {
  static const String _definedBaseUrl = String.fromEnvironment('API_BASE_URL');

  static String get apiBaseUrl => resolveApiBaseUrl(
    defined: _definedBaseUrl,
    platform: defaultTargetPlatform,
    isWeb: kIsWeb,
    isRelease: kReleaseMode,
  );

  /// Dev default matches the contract server (port 5080). Android emulators
  /// reach the host machine at 10.0.2.2. A release build without a defined
  /// URL throws instead of silently targeting localhost.
  static String resolveApiBaseUrl({
    required String defined,
    required TargetPlatform platform,
    required bool isWeb,
    required bool isRelease,
  }) {
    if (defined.isNotEmpty) return defined;
    if (isRelease) {
      throw StateError(
        'API_BASE_URL dart-define is required in release builds',
      );
    }
    if (!isWeb && platform == TargetPlatform.android) {
      return 'http://10.0.2.2:5080';
    }
    return 'http://localhost:5080';
  }

  /// Google OAuth client ids for native sign-in (dart-defines). The server
  /// client id (the web client) is the token audience on Android; the iOS
  /// client id on iOS. Both must be in the server's `Auth:Google:ClientIds`.
  static const String googleClientId = String.fromEnvironment(
    'GOOGLE_CLIENT_ID',
  );
  static const String googleServerClientId = String.fromEnvironment(
    'GOOGLE_SERVER_CLIENT_ID',
  );

  static const bool _definedFakeSso = bool.fromEnvironment('VELA_FAKE_SSO');

  /// Development only: skip the Apple and Google SDKs and sign in with the
  /// server's fake identity provider. Always false in release builds.
  static bool get fakeSso => !kReleaseMode && _definedFakeSso;

  /// Value of the `X-Platform` header.
  static String get platformHeader =>
      resolvePlatformHeader(defaultTargetPlatform, kIsWeb);

  static String resolvePlatformHeader(TargetPlatform platform, bool isWeb) {
    if (isWeb) return 'web';
    return platform == TargetPlatform.iOS ? 'ios' : 'android';
  }
}
