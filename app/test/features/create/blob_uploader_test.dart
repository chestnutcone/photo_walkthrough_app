import 'dart:typed_data';

import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:vela_app/features/create/blob_uploader.dart';

import '../../core/support/fakes.dart';

void main() {
  Dio dioWith(FakeAdapter a) => Dio()..httpClientAdapter = a;
  final url = Uri.parse('https://blob.test/w1?sig=SECRET');
  final bytes = Uint8List.fromList([1, 2, 3]);

  test('puts bytes with block-blob headers and no bearer', () async {
    final adapter = FakeAdapter((_) => jsonResponse(201, {}));
    await DioBlobUploader(
      dioWith(adapter),
      retryDelay: (_) async {},
    ).upload(url: url, bytes: bytes, contentType: 'image/jpeg');
    final r = adapter.requests.single;
    expect(r.method, 'PUT');
    expect(r.headers['x-ms-blob-type'], 'BlockBlob');
    expect(r.headers['content-type'], 'image/jpeg');
    expect(r.headers.containsKey('Authorization'), isFalse);
    expect(r.uri, url);
  });

  test('retries a 503 and then succeeds', () async {
    var n = 0;
    final adapter = FakeAdapter(
      (_) => ++n < 3 ? jsonResponse(503, {}) : jsonResponse(201, {}),
    );
    await DioBlobUploader(
      dioWith(adapter),
      retryDelay: (_) async {},
    ).upload(url: url, bytes: bytes, contentType: 'image/png');
    expect(adapter.requests, hasLength(3));
  });

  test('does not retry a 403 (expired signature)', () async {
    final adapter = FakeAdapter((_) => jsonResponse(403, {}));
    final up = DioBlobUploader(dioWith(adapter), retryDelay: (_) async {});
    await expectLater(
      up.upload(url: url, bytes: bytes, contentType: 'image/png'),
      throwsA(
        isA<BlobUploadException>().having((e) => e.expired, 'expired', isTrue),
      ),
    );
    expect(adapter.requests, hasLength(1));
  });

  test(
    'gives up after repeated network errors without leaking the url',
    () async {
      final adapter = FakeAdapter(
        (o) =>
            throw DioException.connectionError(requestOptions: o, reason: 'x'),
      );
      final up = DioBlobUploader(dioWith(adapter), retryDelay: (_) async {});
      await expectLater(
        up.upload(url: url, bytes: bytes, contentType: 'image/png'),
        throwsA(
          isA<BlobUploadException>().having(
            (e) => e.toString(),
            'text',
            isNot(contains('SECRET')),
          ),
        ),
      );
      expect(adapter.requests, hasLength(3));
    },
  );
}
