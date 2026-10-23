//
// AUTO-GENERATED FILE, DO NOT MODIFY!
//

import 'dart:async';

// ignore: unused_import
import 'dart:convert';
import 'package:vela_api_client/src/deserialize.dart';
import 'package:dio/dio.dart';

import 'package:vela_api_client/src/model/dmca_notice_ack_envelope.dart';
import 'package:vela_api_client/src/model/envelope.dart';
import 'package:vela_api_client/src/model/submit_counter_notice_request.dart';
import 'package:vela_api_client/src/model/submit_dmca_notice_request.dart';

class LegalApi {

  final Dio _dio;

  const LegalApi(this._dio);

  /// Send a counter-notice for a story removed after a copyright notice
  /// Owner only, for a story that is currently removed after a takedown notice. Any other story (not yours, not removed, no takedown notice, unknown id) answers the same 404 work_not_found. One counter-notice per story (409 counter_notice_exists). Free text is stored verbatim; control characters other than new lines are rejected. The body may be at most 20 KB.
  ///
  /// Parameters:
  /// * [id] - Resource id
  /// * [submitCounterNoticeRequest] 
  /// * [cancelToken] - A [CancelToken] that can be used to cancel the operation
  /// * [headers] - Can be used to add additional headers to the request
  /// * [extras] - Can be used to add flags to the request
  /// * [validateStatus] - A [ValidateStatus] callback that can be used to determine request success based on the HTTP status of the response
  /// * [onSendProgress] - A [ProgressCallback] that can be used to get the send progress
  /// * [onReceiveProgress] - A [ProgressCallback] that can be used to get the receive progress
  ///
  /// Returns a [Future] containing a [Response] with a [DmcaNoticeAckEnvelope] as data
  /// Throws [DioException] if API call or serialization fails
  Future<Response<DmcaNoticeAckEnvelope>> submitCounterNotice({ 
    required String id,
    required SubmitCounterNoticeRequest submitCounterNoticeRequest,
    CancelToken? cancelToken,
    Map<String, dynamic>? headers,
    Map<String, dynamic>? extra,
    ValidateStatus? validateStatus,
    ProgressCallback? onSendProgress,
    ProgressCallback? onReceiveProgress,
  }) async {
    final _path = r'/v1/works/{id}/counter-notice'.replaceAll('{' r'id' '}', id.toString());
    final _options = Options(
      method: r'POST',
      headers: <String, dynamic>{
        ...?headers,
      },
      extra: <String, dynamic>{
        'secure': <Map<String, String>>[
          {
            'type': 'http',
            'scheme': 'bearer',
            'name': 'bearerAuth',
          },
        ],
        ...?extra,
      },
      contentType: 'application/json',
      validateStatus: validateStatus,
    );

    dynamic _bodyData;

    try {
_bodyData=jsonEncode(submitCounterNoticeRequest);
    } catch(error, stackTrace) {
      throw DioException(
         requestOptions: _options.compose(
          _dio.options,
          _path,
        ),
        type: DioExceptionType.unknown,
        error: error,
        stackTrace: stackTrace,
      );
    }

    final _response = await _dio.request<Object>(
      _path,
      data: _bodyData,
      options: _options,
      cancelToken: cancelToken,
      onSendProgress: onSendProgress,
      onReceiveProgress: onReceiveProgress,
    );

    DmcaNoticeAckEnvelope? _responseData;

    try {
final rawData = _response.data;
_responseData = rawData == null ? null : deserialize<DmcaNoticeAckEnvelope, DmcaNoticeAckEnvelope>(rawData, 'DmcaNoticeAckEnvelope', growable: true);
    } catch (error, stackTrace) {
      throw DioException(
        requestOptions: _response.requestOptions,
        response: _response,
        type: DioExceptionType.unknown,
        error: error,
        stackTrace: stackTrace,
      );
    }

    return Response<DmcaNoticeAckEnvelope>(
      data: _responseData,
      headers: _response.headers,
      isRedirect: _response.isRedirect,
      requestOptions: _response.requestOptions,
      redirects: _response.redirects,
      statusCode: _response.statusCode,
      statusMessage: _response.statusMessage,
      extra: _response.extra,
    );
  }

  /// Submit a copyright (DMCA) notice
  /// Public. Always answers 202 with a reference id, whether or not the reference matched a story (it never reveals which stories exist); unmatched notices are handled manually. Free text is stored verbatim; control characters other than new lines are rejected. Rate limited per client address (5 per rolling 24 hours, 429 rate_limited with Retry-After). The body may be at most 20 KB (413 payload_too_large). The claimant gets a plain-text acknowledgement email.
  ///
  /// Parameters:
  /// * [submitDmcaNoticeRequest] 
  /// * [cancelToken] - A [CancelToken] that can be used to cancel the operation
  /// * [headers] - Can be used to add additional headers to the request
  /// * [extras] - Can be used to add flags to the request
  /// * [validateStatus] - A [ValidateStatus] callback that can be used to determine request success based on the HTTP status of the response
  /// * [onSendProgress] - A [ProgressCallback] that can be used to get the send progress
  /// * [onReceiveProgress] - A [ProgressCallback] that can be used to get the receive progress
  ///
  /// Returns a [Future] containing a [Response] with a [DmcaNoticeAckEnvelope] as data
  /// Throws [DioException] if API call or serialization fails
  Future<Response<DmcaNoticeAckEnvelope>> submitDmcaNotice({ 
    required SubmitDmcaNoticeRequest submitDmcaNoticeRequest,
    CancelToken? cancelToken,
    Map<String, dynamic>? headers,
    Map<String, dynamic>? extra,
    ValidateStatus? validateStatus,
    ProgressCallback? onSendProgress,
    ProgressCallback? onReceiveProgress,
  }) async {
    final _path = r'/v1/legal/dmca-notices';
    final _options = Options(
      method: r'POST',
      headers: <String, dynamic>{
        ...?headers,
      },
      extra: <String, dynamic>{
        'secure': <Map<String, String>>[],
        ...?extra,
      },
      contentType: 'application/json',
      validateStatus: validateStatus,
    );

    dynamic _bodyData;

    try {
_bodyData=jsonEncode(submitDmcaNoticeRequest);
    } catch(error, stackTrace) {
      throw DioException(
         requestOptions: _options.compose(
          _dio.options,
          _path,
        ),
        type: DioExceptionType.unknown,
        error: error,
        stackTrace: stackTrace,
      );
    }

    final _response = await _dio.request<Object>(
      _path,
      data: _bodyData,
      options: _options,
      cancelToken: cancelToken,
      onSendProgress: onSendProgress,
      onReceiveProgress: onReceiveProgress,
    );

    DmcaNoticeAckEnvelope? _responseData;

    try {
final rawData = _response.data;
_responseData = rawData == null ? null : deserialize<DmcaNoticeAckEnvelope, DmcaNoticeAckEnvelope>(rawData, 'DmcaNoticeAckEnvelope', growable: true);
    } catch (error, stackTrace) {
      throw DioException(
        requestOptions: _response.requestOptions,
        response: _response,
        type: DioExceptionType.unknown,
        error: error,
        stackTrace: stackTrace,
      );
    }

    return Response<DmcaNoticeAckEnvelope>(
      data: _responseData,
      headers: _response.headers,
      isRedirect: _response.isRedirect,
      requestOptions: _response.requestOptions,
      redirects: _response.redirects,
      statusCode: _response.statusCode,
      statusMessage: _response.statusMessage,
      extra: _response.extra,
    );
  }

}
