import 'dart:async';
import 'dart:convert';

import 'package:app_attest/app_attest.dart';
import 'package:crypto/crypto.dart';

import 'attestation_client.dart';

typedef PrepareFn = Future<void> Function(int cloudProjectNumber);
typedef RequestFn = Future<String> Function(String requestHash);

/// Play Integrity Standard request. The server expects
/// `requestHash = base64url(SHA-256(action))` (no padding), so a token cannot
/// move between operations.
class PlayIntegrityAttestationClient implements AttestationClient {
  PlayIntegrityAttestationClient({
    required this.cloudProjectNumber,
    PrepareFn? prepare,
    RequestFn? request,
    this.timeout = attestationTimeout,
  }) : _prepare =
           prepare ??
           ((n) => AppAttest.preparePlayIntegrityTokenProvider(
             cloudProjectNumber: n,
           )),
       _request =
           request ??
           ((hash) =>
               AppAttest.requestStandardPlayIntegrityToken(requestHash: hash));

  /// Google Cloud project number linked to Play Integrity; 0 = not configured.
  final int cloudProjectNumber;
  final Duration timeout;
  final PrepareFn _prepare;
  final RequestFn _request;
  Future<void>? _prepared;

  @override
  Future<String?> token(String action) async {
    if (cloudProjectNumber <= 0) return null;
    try {
      return await _mint(action).timeout(timeout);
    } on TimeoutException {
      _prepared = null; // a hung warm-up must not block every later call
      return null;
    } on Object {
      return null;
    }
  }

  Future<String> _mint(String action) async {
    await _ensurePrepared();
    return _request(requestHash(action));
  }

  /// The warm-up is reusable; only the tokens are single use. A failed warm-up
  /// is forgotten so the next call tries again.
  Future<void> _ensurePrepared() {
    final inFlight = _prepared ??= _prepare(cloudProjectNumber);
    return inFlight.catchError((Object e) {
      _prepared = null;
      throw e;
    });
  }

  static String requestHash(String action) => base64Url
      .encode(sha256.convert(utf8.encode(action)).bytes)
      .replaceAll('=', '');
}
