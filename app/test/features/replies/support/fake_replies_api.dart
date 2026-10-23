import 'dart:async';

import 'package:dio/dio.dart';
import 'package:vela_api_client/vela_api_client.dart';

Reply reply(
  String id, {
  String authorId = 'u2',
  String name = 'Teo',
  String? body,
  bool hidden = false,
}) => Reply.fromJson({
  'id': id,
  'workId': 'w1',
  'chapterIndex': 0,
  'author': {
    'id': authorId,
    'handle': name.toLowerCase(),
    'displayName': name,
    'bio': '',
    'followerCount': 0,
    'followingCount': 0,
    'workCount': 0,
    'onboardingComplete': true,
  },
  'body': body ?? 'text $id',
  'createdAt': '2026-10-01T10:00:00Z',
  if (hidden) 'hiddenForOthers': true,
});

DioException apiError(String code, {int status = 400, String? retryAfter}) {
  final ro = RequestOptions();
  return DioException(
    requestOptions: ro,
    response: Response(
      requestOptions: ro,
      statusCode: status,
      headers: Headers.fromMap({
        if (retryAfter != null) 'retry-after': [retryAfter],
      }),
      data: {
        'error': {'code': code, 'message': 'm'},
      },
    ),
  );
}

class ListCall {
  ListCall(this.workId, this.index, this.cursor);
  final String workId;
  final int index;
  final String? cursor;
  final Completer<ReplyListEnvelope> done = Completer();

  void reply(List<Reply> items, {String? next}) => done.complete(
    ReplyListEnvelope(
      success: true,
      data: items,
      error: null,
      page: Page(nextCursor: next),
    ),
  );
}

class AddCall {
  AddCall(this.body);
  final String body;
  final Completer<ReplyEnvelope> done = Completer();

  void succeed(Reply r) => done.complete(
    ReplyEnvelope(success: true, data: r, error: null, page: null),
  );
  void fail(DioException e) => done.completeError(e);
}

class DeleteCall {
  DeleteCall(this.id);
  final String id;
  final Completer<Envelope> done = Completer();
  void succeed() => done.complete(
    Envelope(success: true, data: null, error: null, page: null),
  );
  void fail(DioException e) => done.completeError(e);
}

/// Ignores cancel tokens on purpose so tests can answer abandoned requests.
class FakeRepliesApi implements VelaApiClient {
  final listCalls = <ListCall>[];
  final addCalls = <AddCall>[];
  final deleteCalls = <DeleteCall>[];

  /// Answers every list call at once with these items (first page only).
  List<Reply>? autoList;
  String meId = 'me';

  /// When set, /me fails with a server error.
  bool meFails = false;

  /// When set, /me waits for it.
  Completer<void>? meGate;

  @override
  RepliesApi getRepliesApi() => _FakeReplies(this);

  @override
  MeApi getMeApi() => _FakeMe(this);

  @override
  dynamic noSuchMethod(Invocation invocation) => super.noSuchMethod(invocation);
}

class _FakeMe implements MeApi {
  _FakeMe(this.api);
  final FakeRepliesApi api;

  @override
  Future<Response<MeEnvelope>> getMe({
    CancelToken? cancelToken,
    Map<String, dynamic>? headers,
    Map<String, dynamic>? extra,
    ValidateStatus? validateStatus,
    ProgressCallback? onSendProgress,
    ProgressCallback? onReceiveProgress,
  }) async {
    final gate = api.meGate;
    if (gate != null) await gate.future;
    if (api.meFails) throw apiError('server_error', status: 500);
    return Response(
      requestOptions: RequestOptions(),
      data: MeEnvelope.fromJson({
        'success': true,
        'data': {
          'id': api.meId,
          'profile': null,
          'onboardingComplete': true,
          'tier': {'name': 'free', 'maxWorks': 20},
          'usedWorks': 0,
        },
        'error': null,
        'page': null,
      }),
    );
  }

  @override
  dynamic noSuchMethod(Invocation invocation) => super.noSuchMethod(invocation);
}

class _FakeReplies implements RepliesApi {
  _FakeReplies(this.api);
  final FakeRepliesApi api;

  @override
  Future<Response<ReplyListEnvelope>> listReplies({
    required String id,
    required int index,
    String? cursor,
    int? limit = 20,
    CancelToken? cancelToken,
    Map<String, dynamic>? headers,
    Map<String, dynamic>? extra,
    ValidateStatus? validateStatus,
    ProgressCallback? onSendProgress,
    ProgressCallback? onReceiveProgress,
  }) async {
    final call = ListCall(id, index, cursor);
    api.listCalls.add(call);
    final auto = api.autoList;
    if (auto != null && cursor == null) call.reply(auto);
    return Response(
      requestOptions: RequestOptions(),
      data: await call.done.future,
    );
  }

  @override
  Future<Response<ReplyEnvelope>> addReply({
    required String id,
    required int index,
    required AddReplyRequest addReplyRequest,
    CancelToken? cancelToken,
    Map<String, dynamic>? headers,
    Map<String, dynamic>? extra,
    ValidateStatus? validateStatus,
    ProgressCallback? onSendProgress,
    ProgressCallback? onReceiveProgress,
  }) async {
    final call = AddCall(addReplyRequest.body);
    api.addCalls.add(call);
    return Response(
      requestOptions: RequestOptions(),
      data: await call.done.future,
    );
  }

  @override
  Future<Response<Envelope>> deleteReply({
    required String id,
    CancelToken? cancelToken,
    Map<String, dynamic>? headers,
    Map<String, dynamic>? extra,
    ValidateStatus? validateStatus,
    ProgressCallback? onSendProgress,
    ProgressCallback? onReceiveProgress,
  }) async {
    final call = DeleteCall(id);
    api.deleteCalls.add(call);
    return Response(
      requestOptions: RequestOptions(),
      data: await call.done.future,
    );
  }

  @override
  dynamic noSuchMethod(Invocation invocation) => super.noSuchMethod(invocation);
}
