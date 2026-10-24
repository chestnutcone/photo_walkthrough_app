import 'dart:async';

import 'package:dio/dio.dart';
import 'package:vela_api_client/vela_api_client.dart';

import '../../search/support/fake_search_api.dart' show searchProfile;

Map<String, Object?> notifJson(
  String id, {
  String type = 'reply',
  String actorId = 'a1',
  String? name,
  String? workId = 'w1',
  String? title = 'Salt and Rope',
  int? chapter = 1,
  bool read = false,
  int? count,
  List<String>? ids,
  String createdAt = '2026-10-22T10:00:00Z',
}) => {
  'id': id,
  'type': type,
  'actor': searchProfile(actorId, name: name ?? 'Ines $actorId'),
  'workId': ?workId,
  'workTitle': ?title,
  'chapterIndex': ?chapter,
  'createdAt': createdAt,
  'read': read,
  'count': ?count,
  'ids': ?ids,
};

/// One `listNotifications` call; the fake ignores the cancel token.
class ListCall {
  ListCall(this.cursor, this.cancelToken);
  final String? cursor;
  final CancelToken? cancelToken;
  final Completer<NotificationListEnvelope> _done = Completer();

  void reply(List<Map<String, Object?>> items, {String? next}) =>
      _done.complete(
        NotificationListEnvelope(
          success: true,
          data: [for (final j in items) Notification.fromJson(j)],
          error: null,
          page: Page(nextCursor: next),
        ),
      );

  void fail() => _done.completeError(
    DioException.connectionError(requestOptions: RequestOptions(), reason: 'x'),
  );

  void failServer() => _done.completeError(
    DioException(
      requestOptions: RequestOptions(),
      response: Response(
        requestOptions: RequestOptions(),
        statusCode: 500,
        data: {
          'error': {'code': 'server_error', 'message': 'm'},
        },
      ),
    ),
  );
}

class MarkCall {
  MarkCall(this.ids, this.all);
  final List<String>? ids;
  final bool? all;
  final Completer<Envelope> _done = Completer();

  void succeed() => _done.complete(
    Envelope(success: true, data: null, error: null, page: null),
  );

  void fail() => _done.completeError(
    DioException.connectionError(requestOptions: RequestOptions(), reason: 'x'),
  );
}

class FakeNotificationsApi implements VelaApiClient {
  final listCalls = <ListCall>[];
  final markCalls = <MarkCall>[];
  int unreadCalls = 0;

  /// What `unreadCount` answers; tests change it to model the server.
  int unread = 0;

  /// When set, every list call is answered at once.
  List<Map<String, Object?>> Function(String? cursor)? auto;

  @override
  NotificationsApi getNotificationsApi() => _Fake(this);

  @override
  dynamic noSuchMethod(Invocation invocation) => super.noSuchMethod(invocation);
}

class _Fake implements NotificationsApi {
  _Fake(this.api);
  final FakeNotificationsApi api;

  @override
  Future<Response<NotificationListEnvelope>> listNotifications({
    String? cursor,
    int? limit = 20,
    CancelToken? cancelToken,
    Map<String, dynamic>? headers,
    Map<String, dynamic>? extra,
    ValidateStatus? validateStatus,
    ProgressCallback? onSendProgress,
    ProgressCallback? onReceiveProgress,
  }) async {
    final call = ListCall(cursor, cancelToken);
    api.listCalls.add(call);
    final auto = api.auto;
    if (auto != null) call.reply(auto(cursor));
    return Response(
      requestOptions: RequestOptions(),
      data: await call._done.future,
    );
  }

  @override
  Future<Response<Envelope>> markRead({
    required MarkReadRequest markReadRequest,
    CancelToken? cancelToken,
    Map<String, dynamic>? headers,
    Map<String, dynamic>? extra,
    ValidateStatus? validateStatus,
    ProgressCallback? onSendProgress,
    ProgressCallback? onReceiveProgress,
  }) async {
    final call = MarkCall(markReadRequest.ids, markReadRequest.all);
    api.markCalls.add(call);
    return Response(
      requestOptions: RequestOptions(),
      data: await call._done.future,
    );
  }

  @override
  Future<Response<UnreadCountEnvelope>> unreadCount({
    CancelToken? cancelToken,
    Map<String, dynamic>? headers,
    Map<String, dynamic>? extra,
    ValidateStatus? validateStatus,
    ProgressCallback? onSendProgress,
    ProgressCallback? onReceiveProgress,
  }) async {
    api.unreadCalls++;
    return Response(
      requestOptions: RequestOptions(),
      data: UnreadCountEnvelope.fromJson({
        'success': true,
        'data': {'count': api.unread},
        'error': null,
        'page': null,
      }),
    );
  }

  @override
  dynamic noSuchMethod(Invocation invocation) => super.noSuchMethod(invocation);
}
