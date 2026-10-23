import 'package:test/test.dart';
import 'package:vela_api_client/vela_api_client.dart';


/// tests for AuthApi
void main() {
  final instance = VelaApiClient().getAuthApi();

  group(AuthApi, () {
    // Request an email one-time code
    //
    // Errors: 400 invalid_email; 400 disposable_email for throwaway-mailbox domains; 403 attestation_required / attestation_failed when the server enforces app attestation; 429 rate_limited (5 per email and 20 per IP per hour).
    //
    //Future<Envelope> emailRequest(EmailRequestRequest emailRequestRequest, { String xAttestation, String xPlatform }) async
    test('test emailRequest', () async {
      // TODO
    });

    // Verify an email one-time code
    //
    //Future<TokenPairEnvelope> emailVerify(EmailVerifyRequest emailVerifyRequest) async
    test('test emailVerify', () async {
      // TODO
    });

    // Revoke a refresh token
    //
    //Future<Envelope> logout(LogoutRequest logoutRequest) async
    test('test logout', () async {
      // TODO
    });

    // Rotate a refresh token
    //
    //Future<TokenPairEnvelope> refresh(RefreshRequest refreshRequest) async
    test('test refresh', () async {
      // TODO
    });

    // Sign in with Apple or Google
    //
    //Future<TokenPairEnvelope> ssoSignIn(SsoSignInRequest ssoSignInRequest, { String xAttestation, String xPlatform }) async
    test('test ssoSignIn', () async {
      // TODO
    });

  });
}
