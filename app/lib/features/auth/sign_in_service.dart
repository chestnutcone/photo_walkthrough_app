import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:vela_api_client/vela_api_client.dart';

import '../../core/api/api_exception.dart';
import '../../core/api/api_providers.dart';
import '../../core/config.dart';
import '../portfolio/portfolio_data.dart';
import 'auth_messages.dart';
import 'onboarding_flag.dart';
import 'sso_client.dart';

final signInServiceProvider = Provider<SignInService>(SignInService.new);

/// The three ways in. Every method stores tokens only through the session
/// store, never logs them, and never keeps an id token.
class SignInService {
  SignInService(this._ref);

  final Ref _ref;

  AuthApi get _api => _ref.read(apiProvider).getAuthApi();

  /// Apple or Google. Throws [SsoCancelledException] when the user backs out,
  /// [SsoFailureException] when the SDK fails, [ApiException] from the API.
  Future<void> signInWithSso(String provider) async {
    final nonce = generateNonce(); // single use: one per attempt
    final idToken = await _ref.read(ssoClientProvider).idToken(provider, nonce);
    final TokenPair pair = AppConfig.fakeSso
        ? await _fakeSso(idToken, nonce)
        : await _ssoCall(provider, idToken, nonce);
    await _store(pair);
  }

  Future<TokenPair> _ssoCall(String provider, String idToken, String nonce) {
    final wire = provider == 'apple'
        ? SsoSignInRequestProviderEnum.apple
        : SsoSignInRequestProviderEnum.google;
    return apiGuard(
      () => _api.ssoSignIn(
        ssoSignInRequest: SsoSignInRequest(
          provider: wire,
          idToken: idToken,
          nonce: nonce,
        ),
      ),
    ).then(
      (res) => unwrapEnvelope(
        success: res.data?.success ?? false,
        data: res.data?.data,
        error: res.data?.error,
      ),
    );
  }

  /// Development only: the generated enum has no "fake" provider.
  Future<TokenPair> _fakeSso(String idToken, String nonce) async {
    final res = await apiGuard(
      () => _ref
          .read(dioProvider)
          .post<Map<String, dynamic>>(
            '/v1/auth/sso',
            data: {'provider': 'fake', 'idToken': idToken, 'nonce': nonce},
            options: Options(contentType: Headers.jsonContentType),
          ),
    );
    final data = res.data?['data'];
    if (data is! Map<String, dynamic>) {
      throw const ApiException(code: 'empty_response', message: '');
    }
    return TokenPair.fromJson(data);
  }

  Future<void> requestEmailCode(String email) async {
    final res = await apiGuard(
      () => _api.emailRequest(
        emailRequestRequest: EmailRequestRequest(email: normalizeEmail(email)),
      ),
    );
    final env = res.data;
    if (env == null || !env.success) {
      throw ApiException(
        code: env?.error?.code ?? 'unknown_error',
        message: '',
      );
    }
  }

  Future<void> verifyEmailCode(String email, String code) async {
    final res = await apiGuard(
      () => _api.emailVerify(
        emailVerifyRequest: EmailVerifyRequest(
          email: normalizeEmail(email),
          code: code,
        ),
      ),
    );
    await _store(
      unwrapEnvelope(
        success: res.data?.success ?? false,
        data: res.data?.data,
        error: res.data?.error,
      ),
    );
  }

  /// Session first, then `/v1/me` so the router already knows whether the
  /// account still has to onboard when the sheet closes. A failed lookup is
  /// not a failed sign-in: [newUserHintProvider] covers it.
  Future<void> _store(TokenPair pair) async {
    // The initial storage read must finish first or it would overwrite the
    // session set below.
    await _ref.read(sessionProvider.future);
    await _ref
        .read(sessionProvider.notifier)
        .signIn(
          accessToken: pair.accessToken,
          refreshToken: pair.refreshToken,
          expiresIn: pair.expiresIn,
        );
    _ref.read(newUserHintProvider.notifier).set(pair.isNewUser);
    try {
      await _ref.read(meProvider.future);
    } on Object {
      // intentionally ignored
    }
  }
}
