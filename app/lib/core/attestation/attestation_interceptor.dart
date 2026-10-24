import 'package:dio/dio.dart';

import '../config.dart';
import 'attestation_client.dart';

/// Adds `X-Attestation` (and `X-Platform`) to the three protected calls. A
/// new token is requested for every attempt, retries included.
class AttestationInterceptor extends Interceptor {
  AttestationInterceptor(this.client);

  final AttestationClient client;

  @override
  Future<void> onRequest(
    RequestOptions options,
    RequestInterceptorHandler handler,
  ) async {
    final action = AttestationActions.forRequest(options.method, options.path);
    if (action != null) {
      final token = await _mint(action);
      if (token != null && token.isNotEmpty) {
        options.headers['X-Attestation'] = token;
        options.headers['X-Platform'] = AppConfig.platformHeader;
      } else {
        // A retry copies the first attempt's headers; that token is spent.
        options.headers.remove('X-Attestation');
      }
    }
    handler.next(options);
  }

  Future<String?> _mint(String action) async {
    try {
      return await client.token(action);
    } on Object {
      // Never block sign-in on a flaky device service; the server decides.
      return null;
    }
  }
}
