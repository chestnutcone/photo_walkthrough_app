import 'dart:async';

import 'package:dio/dio.dart';
import 'package:vela_api_client/vela_api_client.dart';

import 'feed_harness.dart' show feedWork;

/// One `getFeed` call. The fake ignores the cancel token on purpose, so a
/// test can complete a request the controller already abandoned.
class FeedCall {
  FeedCall(this.tab, this.cursor, this.cancelToken);
  final String tab;
  final String? cursor;
  final CancelToken? cancelToken;
  final Completer<WorkListEnvelope> _done = Completer();

  void reply(List<String> workIds, {String? next, String authorId = 'a1'}) =>
      _done.complete(
        WorkListEnvelope(
          success: true,
          data: [
            for (final id in workIds)
              Work.fromJson(feedWork(id, authorId: authorId)),
          ],
          error: null,
          page: Page(nextCursor: next),
        ),
      );

  void fail() => _done.completeError(
    DioException.connectionError(requestOptions: RequestOptions(), reason: 'x'),
  );
}

/// One follow/unfollow call, completed by hand.
class FollowCall {
  FollowCall(this.authorId, this.follow);
  final String authorId;
  final bool follow;
  final Completer<Envelope> _done = Completer();

  void succeed() => _done.complete(
    Envelope(success: true, data: null, error: null, page: null),
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

class FakeFeedApi implements VelaApiClient {
  final feedCalls = <FeedCall>[];
  final followCalls = <FollowCall>[];

  /// When set, every feed call is answered immediately with these ids.
  List<String> Function(String tab, String? cursor)? auto;
  String autoAuthor = 'a1';

  @override
  WorksApi getWorksApi() => _FakeWorks(this);

  @override
  ArtistsApi getArtistsApi() => _FakeArtists(this);

  @override
  dynamic noSuchMethod(Invocation invocation) => super.noSuchMethod(invocation);
}

class _FakeWorks implements WorksApi {
  _FakeWorks(this.api);
  final FakeFeedApi api;

  @override
  Future<Response<WorkListEnvelope>> getFeed({
    required String tab,
    String? cursor,
    int? limit = 20,
    CancelToken? cancelToken,
    Map<String, dynamic>? headers,
    Map<String, dynamic>? extra,
    ValidateStatus? validateStatus,
    ProgressCallback? onSendProgress,
    ProgressCallback? onReceiveProgress,
  }) async {
    final call = FeedCall(tab, cursor, cancelToken);
    api.feedCalls.add(call);
    final auto = api.auto;
    if (auto != null) call.reply(auto(tab, cursor), authorId: api.autoAuthor);
    final data = await call._done.future;
    return Response(requestOptions: RequestOptions(), data: data);
  }

  @override
  dynamic noSuchMethod(Invocation invocation) => super.noSuchMethod(invocation);
}

class _FakeArtists implements ArtistsApi {
  _FakeArtists(this.api);
  final FakeFeedApi api;

  Future<Response<Envelope>> _call(String id, bool follow) async {
    final call = FollowCall(id, follow);
    api.followCalls.add(call);
    return Response(
      requestOptions: RequestOptions(),
      data: await call._done.future,
    );
  }

  @override
  Future<Response<Envelope>> follow({
    required String id,
    CancelToken? cancelToken,
    Map<String, dynamic>? headers,
    Map<String, dynamic>? extra,
    ValidateStatus? validateStatus,
    ProgressCallback? onSendProgress,
    ProgressCallback? onReceiveProgress,
  }) => _call(id, true);

  @override
  Future<Response<Envelope>> unfollow({
    required String id,
    CancelToken? cancelToken,
    Map<String, dynamic>? headers,
    Map<String, dynamic>? extra,
    ValidateStatus? validateStatus,
    ProgressCallback? onSendProgress,
    ProgressCallback? onReceiveProgress,
  }) => _call(id, false);

  @override
  dynamic noSuchMethod(Invocation invocation) => super.noSuchMethod(invocation);
}
