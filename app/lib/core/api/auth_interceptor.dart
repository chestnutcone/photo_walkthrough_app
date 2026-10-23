import 'package:dio/dio.dart';

import '../auth/auth_controller.dart';

/// Attaches the bearer token, refreshes once on 401 and retries, and turns
/// account_suspended / account_deleted into a signed-out blocked state.
/// Never logs tokens or bodies.
class AuthInterceptor extends Interceptor {
  AuthInterceptor({required this.dio, required this.notifier});

  final Dio dio;
  final SessionNotifier Function() notifier;

  static const _sentTokenKey = 'vela.sentAccessToken';
  static const _retriedKey = 'vela.retried';

  static bool _isAuthPath(String path) => path.contains('/v1/auth/');

  @override
  Future<void> onRequest(
    RequestOptions options,
    RequestInterceptorHandler handler,
  ) async {
    if (!_isAuthPath(options.path)) {
      final session = await notifier().current();
      if (session != null) {
        options.headers['Authorization'] = 'Bearer ${session.accessToken}';
        options.extra[_sentTokenKey] = session.accessToken;
      }
    }
    handler.next(options);
  }

  @override
  Future<void> onError(
    DioException err,
    ErrorInterceptorHandler handler,
  ) async {
    final blocked = _blockReason(err);
    if (blocked != null) {
      await notifier().blockAccount(blocked);
      return handler.next(err);
    }

    final req = err.requestOptions;
    final sent = req.extra[_sentTokenKey] as String?;
    if (err.response?.statusCode != 401 || sent == null) {
      return handler.next(err);
    }
    if (req.extra[_retriedKey] == true) {
      // A freshly refreshed token was still rejected: sign out.
      await notifier().clear();
      return handler.next(err);
    }

    try {
      final fresh = await notifier().sessionAfter401(sent);
      final retry = req.copyWith(
        headers: {
          ...req.headers,
          'Authorization': 'Bearer ${fresh.accessToken}',
        },
        extra: {
          ...req.extra,
          _sentTokenKey: fresh.accessToken,
          _retriedKey: true,
        },
      );
      handler.resolve(await dio.fetch<dynamic>(retry));
    } on SessionExpiredException {
      handler.next(err);
    } on DioException catch (e) {
      handler.next(e);
    }
  }

  static AccountBlock? _blockReason(DioException err) {
    if (err.response?.statusCode != 403) return null;
    final body = err.response?.data;
    final code = body is Map && body['error'] is Map
        ? (body['error'] as Map)['code']
        : null;
    return switch (code) {
      'account_suspended' => AccountBlock.suspended,
      'account_deleted' => AccountBlock.deleted,
      _ => null,
    };
  }
}
