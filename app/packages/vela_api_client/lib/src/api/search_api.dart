//
// AUTO-GENERATED FILE, DO NOT MODIFY!
//

import 'dart:async';

// ignore: unused_import
import 'dart:convert';
import 'package:vela_api_client/src/deserialize.dart';
import 'package:dio/dio.dart';

import 'package:vela_api_client/src/model/envelope.dart';
import 'package:vela_api_client/src/model/search_results_list_envelope.dart';

class SearchApi {

  final Dio _dio;

  const SearchApi(this._dio);

  /// Search artists or stories
  /// Public. &#x60;q&#x60; is 2 to 80 characters after trimming (shorter: 400 query_too_short; longer or containing a null character: 400 validation_failed); whitespace-separated terms (first 5) must all match, with typo tolerance. &#x60;type&#x60; defaults to &#x60;artists&#x60;; &#x60;data&#x60; fills only the list for the searched type and the other is empty. Results are ranked best match first and paged with an opaque cursor (invalid: 400 invalid_cursor). Hidden, blocked, suspended and unpublished content never appears.
  ///
  /// Parameters:
  /// * [q] - Search text
  /// * [type] - What to search
  /// * [cursor] - Opaque cursor from the previous page
  /// * [limit] - Page size, 1 to 50, default 20
  /// * [cancelToken] - A [CancelToken] that can be used to cancel the operation
  /// * [headers] - Can be used to add additional headers to the request
  /// * [extras] - Can be used to add flags to the request
  /// * [validateStatus] - A [ValidateStatus] callback that can be used to determine request success based on the HTTP status of the response
  /// * [onSendProgress] - A [ProgressCallback] that can be used to get the send progress
  /// * [onReceiveProgress] - A [ProgressCallback] that can be used to get the receive progress
  ///
  /// Returns a [Future] containing a [Response] with a [SearchResultsListEnvelope] as data
  /// Throws [DioException] if API call or serialization fails
  Future<Response<SearchResultsListEnvelope>> search({ 
    required String q,
    String? type,
    String? cursor,
    int? limit = 20,
    CancelToken? cancelToken,
    Map<String, dynamic>? headers,
    Map<String, dynamic>? extra,
    ValidateStatus? validateStatus,
    ProgressCallback? onSendProgress,
    ProgressCallback? onReceiveProgress,
  }) async {
    final _path = r'/v1/search';
    final _options = Options(
      method: r'GET',
      headers: <String, dynamic>{
        ...?headers,
      },
      extra: <String, dynamic>{
        'secure': <Map<String, String>>[],
        ...?extra,
      },
      validateStatus: validateStatus,
    );

    final _queryParameters = <String, dynamic>{
      r'q': q,
      if (type != null) r'type': type,
      if (cursor != null) r'cursor': cursor,
      if (limit != null) r'limit': limit,
    };

    final _response = await _dio.request<Object>(
      _path,
      options: _options,
      queryParameters: _queryParameters,
      cancelToken: cancelToken,
      onSendProgress: onSendProgress,
      onReceiveProgress: onReceiveProgress,
    );

    SearchResultsListEnvelope? _responseData;

    try {
final rawData = _response.data;
_responseData = rawData == null ? null : deserialize<SearchResultsListEnvelope, SearchResultsListEnvelope>(rawData, 'SearchResultsListEnvelope', growable: true);
    } catch (error, stackTrace) {
      throw DioException(
        requestOptions: _response.requestOptions,
        response: _response,
        type: DioExceptionType.unknown,
        error: error,
        stackTrace: stackTrace,
      );
    }

    return Response<SearchResultsListEnvelope>(
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
