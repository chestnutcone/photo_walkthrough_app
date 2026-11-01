import 'dart:io' show HttpDate;

import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:vela_api_client/vela_api_client.dart';
import 'package:vela_app/core/api/api_exception.dart';

void main() {
  test('unwrapEnvelope returns data on success', () {
    expect(unwrapEnvelope<int>(success: true, data: 3, error: null), 3);
  });

  test('unwrapEnvelope throws typed ApiException on error body', () {
    expect(
      () => unwrapEnvelope<int>(
        success: false,
        data: null,
        error: ErrorBody(code: 'quota_exceeded', message: 'Full'),
      ),
      throwsA(
        isA<ApiException>()
            .having((e) => e.code, 'code', 'quota_exceeded')
            .having((e) => e.message, 'message', 'Full'),
      ),
    );
  });

  test('unwrapEnvelope throws when success has no data', () {
    expect(
      () => unwrapEnvelope<int>(success: true, data: null, error: null),
      throwsA(isA<ApiException>()),
    );
  });

  test('ApiException.fromDio reads envelope, status and Retry-After', () {
    final opts = RequestOptions(path: '/x');
    final e = DioException.badResponse(
      statusCode: 429,
      requestOptions: opts,
      response: Response<dynamic>(
        requestOptions: opts,
        statusCode: 429,
        headers: Headers.fromMap({
          'retry-after': ['30'],
        }),
        data: {
          'success': false,
          'data': null,
          'error': {'code': 'rate_limited', 'message': 'Slow down'},
          'page': null,
        },
      ),
    );

    final ex = ApiException.fromDio(e);

    expect(ex.code, 'rate_limited');
    expect(ex.message, 'Slow down');
    expect(ex.statusCode, 429);
    expect(ex.retryAfter, const Duration(seconds: 30));
  });

  test('ApiException.fromDio maps network failures to network_error', () {
    final e = DioException.connectionError(
      requestOptions: RequestOptions(path: '/x'),
      reason: 'offline',
    );
    expect(ApiException.fromDio(e).code, 'network_error');
  });

  test('apiGuard converts DioException', () async {
    await expectLater(
      apiGuard<int>(
        () async => throw DioException.connectionError(
          requestOptions: RequestOptions(path: '/x'),
          reason: 'offline',
        ),
      ),
      throwsA(isA<ApiException>()),
    );
  });

  test('Retry-After accepts an HTTP date and clamps negatives', () {
    final opts = RequestOptions(path: '/x');
    DioException make(String v) => DioException.badResponse(
      statusCode: 429,
      requestOptions: opts,
      response: Response<dynamic>(
        requestOptions: opts,
        statusCode: 429,
        headers: Headers.fromMap({
          'retry-after': [v],
        }),
        data: null,
      ),
    );
    final future = DateTime.now().toUtc().add(const Duration(minutes: 5));
    final date = HttpDate.format(future);
    final d = ApiException.fromDio(make(date)).retryAfter!;
    expect(d.inSeconds, inInclusiveRange(290, 300));
    expect(ApiException.fromDio(make('-5')).retryAfter, Duration.zero);
  });
}
