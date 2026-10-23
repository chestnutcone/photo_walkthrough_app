import 'package:flutter/foundation.dart';

/// Build-time configuration. Override with
/// `--dart-define=API_BASE_URL=https://api.example.com`.
abstract final class AppConfig {
  static const String _definedBaseUrl = String.fromEnvironment('API_BASE_URL');

  /// Android emulators reach the host machine at 10.0.2.2.
  static String get apiBaseUrl => resolveApiBaseUrl(
    defined: _definedBaseUrl,
    platform: defaultTargetPlatform,
    isWeb: kIsWeb,
  );

  static String resolveApiBaseUrl({
    required String defined,
    required TargetPlatform platform,
    required bool isWeb,
  }) {
    if (defined.isNotEmpty) return defined;
    if (!isWeb && platform == TargetPlatform.android) {
      return 'http://10.0.2.2:5000';
    }
    return 'http://localhost:5000';
  }

  /// Value of the `X-Platform` header.
  static String get platformHeader =>
      defaultTargetPlatform == TargetPlatform.iOS ? 'ios' : 'android';
}
