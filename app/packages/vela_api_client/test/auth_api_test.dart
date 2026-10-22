import 'package:test/test.dart';
import 'package:vela_api_client/vela_api_client.dart';


/// tests for AuthApi
void main() {
  final instance = VelaApiClient().getAuthApi();

  group(AuthApi, () {
    // Request an email one-time code
    //
    //Future<Envelope> emailRequest(EmailRequestRequest emailRequestRequest) async
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
    //Future<TokenPairEnvelope> ssoSignIn(SsoSignInRequest ssoSignInRequest) async
    test('test ssoSignIn', () async {
      // TODO
    });

  });
}
