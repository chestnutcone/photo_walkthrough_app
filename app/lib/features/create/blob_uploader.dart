import 'dart:typed_data';

import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_providers.dart' show httpClientAdapterProvider;

/// A blob upload that did not succeed. [expired] means the signed URL was
/// refused (403/401), so retrying the same URL is pointless. The message
/// never contains the URL (it carries a signature).
class BlobUploadException implements Exception {
  const BlobUploadException({this.statusCode, this.expired = false});
  final int? statusCode;
  final bool expired;

  @override
  String toString() => 'BlobUploadException(status: $statusCode)';
}

abstract interface class BlobUploader {
  Future<void> upload({
    required Uri url,
    required Uint8List bytes,
    required String contentType,
  });
}

const int _maxAttempts = 3;

/// PUTs to the pre-signed URL on a Dio that has no bearer interceptor: the
/// bytes are a plain buffer, so retrying here is safe and the access token
/// is never sent to the storage host.
class DioBlobUploader implements BlobUploader {
  DioBlobUploader(this._dio, {Future<void> Function(int attempt)? retryDelay})
    : _retryDelay =
          retryDelay ??
          ((attempt) => Future<void>.delayed(Duration(seconds: attempt)));

  final Dio _dio;
  final Future<void> Function(int attempt) _retryDelay;

  @override
  Future<void> upload({
    required Uri url,
    required Uint8List bytes,
    required String contentType,
  }) async {
    for (var attempt = 1; ; attempt++) {
      try {
        await _dio.putUri<void>(
          url,
          data: bytes,
          options: Options(
            headers: {
              'x-ms-blob-type': 'BlockBlob',
              Headers.contentTypeHeader: contentType,
              Headers.contentLengthHeader: bytes.length,
            },
            sendTimeout: const Duration(minutes: 2),
            receiveTimeout: const Duration(seconds: 30),
          ),
        );
        return;
      } on DioException catch (e) {
        final status = e.response?.statusCode;
        if (status == 401 || status == 403) {
          throw BlobUploadException(statusCode: status, expired: true);
        }
        final retryable = status == null || status >= 500 || status == 429;
        if (!retryable || attempt >= _maxAttempts) {
          throw BlobUploadException(statusCode: status);
        }
        await _retryDelay(attempt);
      }
    }
  }
}

final blobUploaderProvider = Provider<BlobUploader>((ref) {
  final dio = Dio(BaseOptions(connectTimeout: const Duration(seconds: 15)));
  final adapter = ref.read(httpClientAdapterProvider);
  if (adapter != null) dio.httpClientAdapter = adapter;
  ref.onDispose(dio.close);
  return DioBlobUploader(dio);
});
