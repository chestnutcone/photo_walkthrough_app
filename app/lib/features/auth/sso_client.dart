import 'dart:convert';
import 'dart:math';

import 'package:crypto/crypto.dart';
import 'package:flutter/foundation.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:google_sign_in/google_sign_in.dart';
import 'package:sign_in_with_apple/sign_in_with_apple.dart';

import '../../core/config.dart';

/// The user closed the Apple or Google sheet. Not an error.
class SsoCancelledException implements Exception {
  const SsoCancelledException();
  @override
  String toString() => 'SsoCancelledException';
}

/// The Apple or Google SDK failed or returned no identity token.
class SsoFailureException implements Exception {
  const SsoFailureException([this.reason = 'sso_failed']);
  final String reason;
  @override
  String toString() => 'SsoFailureException($reason)';
}

/// Gets an OpenID Connect id token from Apple or Google.
///
/// [nonce] is the RAW single-use nonce that is also sent to the API. Each
/// implementation puts the form the provider expects into the token request;
/// the server checks the token's nonce claim against it (see
/// [appleNonceClaim]).
abstract interface class SsoClient {
  Future<String> idToken(String provider, String nonce);
}

/// 32 random bytes, base64url without padding (43 characters).
String generateNonce([Random? random]) {
  final rng = random ?? Random.secure();
  final bytes = List<int>.generate(32, (_) => rng.nextInt(256));
  return base64Url.encode(bytes).replaceAll('=', '');
}

/// What Apple must embed in the token: the lowercase hex SHA-256 of the raw
/// nonce. The API's AppleIdentityVerifier compares the token claim with
/// exactly this value. Google gets the raw nonce unchanged.
String appleNonceClaim(String rawNonce) =>
    sha256.convert(utf8.encode(rawNonce)).toString();

class RealSsoClient implements SsoClient {
  const RealSsoClient();

  @override
  Future<String> idToken(String provider, String nonce) => switch (provider) {
    'apple' => _apple(nonce),
    'google' => _google(nonce),
    _ => throw ArgumentError.value(provider, 'provider'),
  };

  Future<String> _apple(String nonce) async {
    try {
      final credential = await SignInWithApple.getAppleIDCredential(
        scopes: const [],
        nonce: appleNonceClaim(nonce),
      );
      final token = credential.identityToken;
      if (token == null || token.isEmpty) {
        throw const SsoFailureException('apple_no_token');
      }
      return token;
    } on SignInWithAppleAuthorizationException catch (e) {
      if (e.code == AuthorizationErrorCode.canceled) {
        throw const SsoCancelledException();
      }
      throw SsoFailureException('apple_${e.code.name}');
    }
  }

  /// google_sign_in 7 takes the nonce at initialize(); the Android and iOS
  /// platform implementations only store it, so re-initialising per attempt
  /// is how each attempt gets its own nonce. Calls are serialised by the
  /// sign-in sheet's single in-flight attempt.
  Future<String> _google(String nonce) async {
    try {
      await GoogleSignIn.instance.initialize(
        clientId: _orNull(AppConfig.googleClientId),
        serverClientId: _orNull(AppConfig.googleServerClientId),
        nonce: nonce,
      );
      final account = await GoogleSignIn.instance.authenticate();
      final token = account.authentication.idToken;
      if (token == null || token.isEmpty) {
        throw const SsoFailureException('google_no_token');
      }
      return token;
    } on GoogleSignInException catch (e) {
      if (e.code == GoogleSignInExceptionCode.canceled ||
          e.code == GoogleSignInExceptionCode.interrupted) {
        throw const SsoCancelledException();
      }
      throw SsoFailureException('google_${e.code.name}');
    }
  }

  static String? _orNull(String v) => v.isEmpty ? null : v;
}

/// Development stand-in (`--dart-define=VELA_FAKE_SSO=true`). The token format
/// is the one the API's development-only FakeIdentityVerifier accepts.
class DevFakeSsoClient implements SsoClient {
  const DevFakeSsoClient();

  @override
  Future<String> idToken(String provider, String nonce) async =>
      'fake:dev-$provider:dev-$provider@example.com';
}

final ssoClientProvider = Provider<SsoClient>(
  (_) => AppConfig.fakeSso ? const DevFakeSsoClient() : const RealSsoClient(),
);

/// Apple's button is offered on iOS only. Tests override this.
final appleSignInAvailableProvider = Provider<bool>(
  (_) => !kIsWeb && defaultTargetPlatform == TargetPlatform.iOS,
);
