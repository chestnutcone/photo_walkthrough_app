import 'dart:async';

import 'package:dio/dio.dart';
import 'package:vela_api_client/vela_api_client.dart';

import '../../feed/support/feed_harness.dart' show feedWork;

Map<String, Object?> searchProfile(
  String id, {
  String? handle,
  String? name,
  bool? isFollowing = false,
}) => {
  'id': id,
  'handle': handle ?? 'h$id',
  'displayName': name ?? 'Artist $id',
  'bio': '',
  'followerCount': 3,
  'followingCount': 1,
  'workCount': 2,
  'isFollowing': ?isFollowing,
  'onboardingComplete': true,
  'discipline': 'Photographer',
  'city': 'Lisbon',
};

/// One `search` call. The fake ignores the cancel token on purpose, so a test
/// can complete a request the controller already abandoned.
class SearchCall {
  SearchCall(this.q, this.type, this.cursor, this.cancelToken, this.following);
  final bool? following;
  final String q;
  final String? type;
  final String? cursor;
  final CancelToken? cancelToken;
  final Completer<SearchResultsListEnvelope> _done = Completer();

  void reply({
    List<String> artists = const [],
    List<String> stories = const [],
    String? next,
  }) => _done.complete(
    SearchResultsListEnvelope(
      success: true,
      data: SearchResults.fromJson({
        'artists': [
          for (final id in artists) searchProfile(id, isFollowing: following),
        ],
        'stories': [for (final id in stories) feedWork(id)],
      }),
      error: null,
      page: Page(nextCursor: next),
    ),
  );

  void fail({String code = 'server_error', int status = 500}) =>
      _done.completeError(
        DioException(
          requestOptions: RequestOptions(),
          response: Response(
            requestOptions: RequestOptions(),
            statusCode: status,
            data: {
              'error': {'code': code, 'message': 'm'},
            },
          ),
        ),
      );

  void failOffline() => _done.completeError(
    DioException.connectionError(requestOptions: RequestOptions(), reason: 'x'),
  );
}

class SuggestedCall {
  SuggestedCall(this.following);
  final bool? following;
  final Completer<ProfileListEnvelope> _done = Completer();

  void reply(List<String> ids) => _done.complete(
    ProfileListEnvelope(
      success: true,
      data: [
        for (final id in ids)
          Profile.fromJson(searchProfile(id, isFollowing: following)),
      ],
      error: null,
      page: Page(nextCursor: null),
    ),
  );
}

class FollowCall {
  FollowCall(this.id, this.follow);
  final String id;
  final bool follow;
  final Completer<Envelope> _done = Completer();

  void succeed() => _done.complete(
    Envelope(success: true, data: null, error: null, page: null),
  );
}

class FakeSearchApi implements VelaApiClient {
  final searchCalls = <SearchCall>[];
  final suggestedCalls = <SuggestedCall>[];
  final followCalls = <FollowCall>[];

  /// `isFollowing` the server reports; null is what a guest gets.
  bool? following = false;

  /// When true, suggested artists answer at once with a1, a2.
  bool autoSuggested = true;

  /// When set, every search is answered at once.
  void Function(SearchCall call)? auto;

  @override
  SearchApi getSearchApi() => _FakeSearch(this);

  @override
  ArtistsApi getArtistsApi() => _FakeArtists(this);

  @override
  dynamic noSuchMethod(Invocation invocation) => super.noSuchMethod(invocation);
}

class _FakeSearch implements SearchApi {
  _FakeSearch(this.api);
  final FakeSearchApi api;

  @override
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
    final call = SearchCall(q, type, cursor, cancelToken, api.following);
    api.searchCalls.add(call);
    api.auto?.call(call);
    final data = await call._done.future;
    return Response(requestOptions: RequestOptions(), data: data);
  }

  @override
  dynamic noSuchMethod(Invocation invocation) => super.noSuchMethod(invocation);
}

class _FakeArtists implements ArtistsApi {
  _FakeArtists(this.api);
  final FakeSearchApi api;

  @override
  Future<Response<ProfileListEnvelope>> suggestedArtists({
    String? cursor,
    int? limit = 20,
    CancelToken? cancelToken,
    Map<String, dynamic>? headers,
    Map<String, dynamic>? extra,
    ValidateStatus? validateStatus,
    ProgressCallback? onSendProgress,
    ProgressCallback? onReceiveProgress,
  }) async {
    final call = SuggestedCall(api.following);
    api.suggestedCalls.add(call);
    if (api.autoSuggested) call.reply(['s1', 's2']);
    return Response(
      requestOptions: RequestOptions(),
      data: await call._done.future,
    );
  }

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
