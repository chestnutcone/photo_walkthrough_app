import 'package:flutter_riverpod/flutter_riverpod.dart';

/// Mints a one-time proof that the request comes from a genuine copy of the
/// app on a real device. Tokens are single use and short lived: callers ask
/// for a new one per request and never keep it.
///
/// Returns null when attestation is not available or not configured; the
/// request then goes without the header and the server decides.
abstract interface class AttestationClient {
  Future<String?> token(String action);
}

/// Web, desktop and unconfigured builds.
class NoAttestationClient implements AttestationClient {
  const NoAttestationClient();

  @override
  Future<String?> token(String action) async => null;
}

/// Test double: returns [value] and records the requested actions.
class FakeAttestationClient implements AttestationClient {
  FakeAttestationClient([this.value = 'valid']);

  final String? value;
  final List<String> actions = [];

  @override
  Future<String?> token(String action) async {
    actions.add(action);
    return value;
  }
}

/// `main.dart` overrides this with the platform client; the default keeps the
/// web entry point free of the native attestation plugin.
final attestationClientProvider = Provider<AttestationClient>(
  (_) => const NoAttestationClient(),
);

/// Action strings the server binds each token to (`RequireAttestation(...)`).
abstract final class AttestationActions {
  static const emailRequest = 'email_request';
  static const sso = 'sso';
  static const createWork = 'create_work';

  /// The action for a protected request, or null when it needs none. Matches
  /// the exact method and path (no prefix or trailing-slash tolerance).
  static String? forRequest(String method, String path) {
    final bare = path.split('?').first;
    return switch ((method.toUpperCase(), bare)) {
      ('POST', '/v1/auth/email/request') => emailRequest,
      ('POST', '/v1/auth/sso') => sso,
      ('POST', '/v1/works') => createWork,
      _ => null,
    };
  }
}
