import 'dart:async';
import 'dart:convert';

import 'package:app_attest/app_attest.dart';

import 'attestation_client.dart';

typedef AttestKeyFn = Future<String> Function(String keyId, String challenge);

/// iOS App Attest. Token: `b64url(challenge) "." b64url(attestation object)`
/// with `challenge = "{action}|{unix seconds}"`. The plugin hashes the
/// challenge with SHA-256 for Apple's clientDataHash, as the server checks.
///
/// Every token is a fresh key plus a fresh attestation (the server keeps no
/// key store). Apple rate-limits key generation; failures return null.
class AppAttestClient implements AttestationClient {
  AppAttestClient({
    required this.enabled,
    DateTime Function()? clock,
    Future<String> Function()? generateKey,
    AttestKeyFn? attestKey,
    this.timeout = attestationTimeout,
  }) : _clock = clock ?? DateTime.now,
       _generateKey = generateKey ?? AppAttest.generateKey,
       _attestKey =
           attestKey ??
           ((keyId, challenge) async => (await AppAttest.attestKey(
             keyId: keyId,
             challenge: challenge,
           )).attestationObject);

  /// False until the App Attest capability and the dart-define are set up.
  final bool enabled;
  final Duration timeout;
  final DateTime Function() _clock;
  final Future<String> Function() _generateKey;
  final AttestKeyFn _attestKey;

  @override
  Future<String?> token(String action) async {
    if (!enabled) return null;
    try {
      return await _mint(action).timeout(timeout);
    } on Object {
      return null; // includes TimeoutException
    }
  }

  Future<String> _mint(String action) async {
    final seconds = _clock().toUtc().millisecondsSinceEpoch ~/ 1000;
    final challenge = '$action|$seconds';
    final keyId = await _generateKey();
    final attestation = await _attestKey(keyId, challenge);
    return '${_b64url(utf8.encode(challenge))}.'
        '${_b64url(base64.decode(attestation))}';
  }

  static String _b64url(List<int> bytes) =>
      base64Url.encode(bytes).replaceAll('=', '');
}
