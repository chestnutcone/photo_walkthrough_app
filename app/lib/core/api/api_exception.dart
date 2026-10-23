import 'package:dio/dio.dart';
import 'package:vela_api_client/vela_api_client.dart';

/// Typed failure for any API call. [code] is the envelope error code, or
/// `network_error` / `unknown_error` when the server gave none.
class ApiException implements Exception {
  const ApiException({
    required this.code,
    required this.message,
    this.statusCode,
    this.retryAfter,
  });

  final String code;
  final String message;
  final int? statusCode;
  final Duration? retryAfter;

  factory ApiException.fromDio(DioException e) {
    final response = e.response;
    final body = response?.data;
    final error = body is Map ? body['error'] : null;
    final retry = int.tryParse(response?.headers.value('retry-after') ?? '');
    if (error is Map && error['code'] is String) {
      return ApiException(
        code: error['code'] as String,
        message: (error['message'] as String?) ?? '',
        statusCode: response?.statusCode,
        retryAfter: retry == null ? null : Duration(seconds: retry),
      );
    }
    return ApiException(
      code: response == null ? 'network_error' : 'unknown_error',
      message: e.message ?? '',
      statusCode: response?.statusCode,
      retryAfter: retry == null ? null : Duration(seconds: retry),
    );
  }

  @override
  String toString() => 'ApiException($code, status: $statusCode)';
}

/// Returns the payload of an envelope or throws [ApiException].
T unwrapEnvelope<T>({
  required bool success,
  required T? data,
  required ErrorBody? error,
}) {
  if (!success || error != null) {
    throw ApiException(
      code: error?.code ?? 'unknown_error',
      message: error?.message ?? '',
    );
  }
  if (data == null) {
    throw const ApiException(
      code: 'empty_response',
      message: 'Response had no data',
    );
  }
  return data;
}

/// Runs [call], converting transport errors into [ApiException].
Future<T> apiGuard<T>(Future<T> Function() call) async {
  try {
    return await call();
  } on DioException catch (e) {
    throw ApiException.fromDio(e);
  }
}
