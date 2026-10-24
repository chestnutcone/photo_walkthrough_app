import 'package:flutter/foundation.dart';

import 'branding.dart';

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
    // A release build talks only to https: tokens and photos must never cross the network in clear text, and a mistyped
    // define should fail at start-up, not silently on the first request.
    if (defined.isNotEmpty) {
      if (isRelease && !defined.toLowerCase().startsWith('https://')) {
        throw StateError('API_BASE_URL must start with https:// in release builds');
      }
      return defined;
    }
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

  /// Where "Contact support" writes to. Placeholder domain until the final
  /// name exists; override with `--dart-define=SUPPORT_EMAIL=...`.
  static const String supportEmail = String.fromEnvironment(
    'SUPPORT_EMAIL',
    defaultValue: 'support@${Branding.domain}',
  );

  /// Shown in Settings. Keep in step with pubspec.yaml (`--dart-define`
  /// `APP_VERSION` overrides it in CI builds).
  static const String appVersion = String.fromEnvironment(
    'APP_VERSION',
    defaultValue: '1.0.0',
  );

  /// Host of the universal / app links (`https://{LINKS_DOMAIN}/s/{workId}`).
  /// Must match the host in Runner.entitlements and the Android manifest.
  static const String linksDomain = String.fromEnvironment(
    'LINKS_DOMAIN',
    defaultValue: Branding.domain,
  );

  /// Store listings for the web viewer's "Open in app" banner. Empty hides
  /// the matching button.
  static const String appStoreUrl = String.fromEnvironment('APP_STORE_URL');
  static const String playStoreUrl = String.fromEnvironment('PLAY_STORE_URL');

  /// Google Cloud project number linked to Play Integrity (Android app
  /// attestation). 0 until the owner sets it; tokens are then skipped.
  static const int playCloudProjectNumber = int.fromEnvironment(
    'PLAY_CLOUD_PROJECT_NUMBER',
  );

  /// Turn on iOS App Attest once the capability and the server's
  /// `Abuse:AppAttest` settings exist (`--dart-define=APP_ATTEST_ENABLED=true`).
  static const bool appAttestEnabled = bool.fromEnvironment(
    'APP_ATTEST_ENABLED',
  );

  /// Sentry DSN for crash reports. Empty disables crash reporting.
  static const String sentryDsn = String.fromEnvironment('SENTRY_DSN');
}
