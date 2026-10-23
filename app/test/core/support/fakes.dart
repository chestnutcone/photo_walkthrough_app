import 'dart:async';
import 'dart:convert';
import 'dart:typed_data';

import 'package:dio/dio.dart';
import 'package:vela_app/core/auth/session_store.dart';

/// In-memory [SessionStorage] that survives container re-creation when the
/// same instance is reused.
class FakeSessionStorage implements SessionStorage {
  FakeSessionStorage([this.value]);
  String? value;

  @override
  Future<String?> read() async => value;
  @override
  Future<void> write(String json) async => value = json;
  @override
  Future<void> delete() async => value = null;
}

typedef FakeHandler = FutureOr<ResponseBody> Function(RequestOptions options);

class FakeAdapter implements HttpClientAdapter {
  FakeAdapter(this.handler);
  final FakeHandler handler;
  final List<RequestOptions> requests = [];

  int count(String path) => requests.where((r) => r.path == path).length;

  @override
  Future<ResponseBody> fetch(
    RequestOptions options,
    Stream<Uint8List>? requestStream,
    Future<void>? cancelFuture,
  ) async {
    requests.add(options);
    return handler(options);
  }

  @override
  void close({bool force = false}) {}
}

ResponseBody jsonResponse(
  int status,
  Object body, {
  Map<String, List<String>> headers = const {},
}) {
  return ResponseBody.fromString(
    jsonEncode(body),
    status,
    headers: {
      Headers.contentTypeHeader: [Headers.jsonContentType],
      ...headers,
    },
  );
}

Map<String, Object?> okEnvelope(Object? data) => {
  'success': true,
  'data': data,
  'error': null,
  'page': null,
};

Map<String, Object?> errEnvelope(String code, [String message = 'm']) => {
  'success': false,
  'data': null,
  'error': {'code': code, 'message': message},
  'page': null,
};

Map<String, Object?> tokenPair(
  String access,
  String refresh, {
  int expiresIn = 900,
}) => okEnvelope({
  'accessToken': access,
  'refreshToken': refresh,
  'expiresIn': expiresIn,
  'isNewUser': false,
});
