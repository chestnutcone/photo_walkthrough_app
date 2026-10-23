import 'dart:async';

import 'package:dio/dio.dart';
import 'package:vela_api_client/vela_api_client.dart';

import '../../../core/support/fakes.dart' show okEnvelope;
import 'portfolio_harness.dart' show listPage, meJson, profileJson;

/// A call held open until the test completes it. The fake ignores the cancel
/// token on purpose, so a test can complete a request the app already
/// abandoned (Dio itself would drop such a response).
class HeldCall<T> {
  HeldCall(this.key, this.cursor);
  final String key;
  final String? cursor;
  final Completer<T> done = Completer();
}

Response<T> _ok<T>(T data) =>
    Response(requestOptions: RequestOptions(), statusCode: 200, data: data);

class FakePortfolioApi implements VelaApiClient {
  final artist = <HeldCall<ProfileEnvelope>>[];
  final works = <HeldCall<WorkListEnvelope>>[];
  final followers = <HeldCall<ProfileListEnvelope>>[];

  @override
  ArtistsApi getArtistsApi() => _Artists(this);

  @override
  MeApi getMeApi() => _Me();

  @override
  dynamic noSuchMethod(Invocation invocation) => super.noSuchMethod(invocation);

  static ProfileEnvelope profile({
    String handle = 'ines',
    String name = 'Ines',
  }) => ProfileEnvelope.fromJson(
    okEnvelope(profileJson(handle: handle, name: name)),
  );

  static WorkListEnvelope workList(List<Map<String, Object?>> works) =>
      WorkListEnvelope.fromJson(listPage(works));

  static ProfileListEnvelope followerList(List<Map<String, Object?>> rows) =>
      ProfileListEnvelope.fromJson(listPage(rows));
}

class _Me implements MeApi {
  @override
  Future<Response<MeEnvelope>> getMe({
    CancelToken? cancelToken,
    Map<String, dynamic>? headers,
    Map<String, dynamic>? extra,
    ValidateStatus? validateStatus,
    ProgressCallback? onSendProgress,
    ProgressCallback? onReceiveProgress,
  }) async => _ok(MeEnvelope.fromJson(okEnvelope(meJson())));

  @override
  dynamic noSuchMethod(Invocation invocation) => super.noSuchMethod(invocation);
}

class _Artists implements ArtistsApi {
  _Artists(this.api);
  final FakePortfolioApi api;

  Future<Response<T>> _held<T>(
    List<HeldCall<T>> list,
    String key,
    String? cursor,
  ) {
    final call = HeldCall<T>(key, cursor);
    list.add(call);
    return call.done.future.then(_ok);
  }

  @override
  Future<Response<ProfileEnvelope>> getArtist({
    required String handle,
    CancelToken? cancelToken,
    Map<String, dynamic>? headers,
    Map<String, dynamic>? extra,
    ValidateStatus? validateStatus,
    ProgressCallback? onSendProgress,
    ProgressCallback? onReceiveProgress,
  }) => _held(api.artist, handle, null);

  @override
  Future<Response<WorkListEnvelope>> listArtistWorks({
    required String handle,
    String? cursor,
    int? limit = 20,
    CancelToken? cancelToken,
    Map<String, dynamic>? headers,
    Map<String, dynamic>? extra,
    ValidateStatus? validateStatus,
    ProgressCallback? onSendProgress,
    ProgressCallback? onReceiveProgress,
  }) => _held(api.works, handle, cursor);

  @override
  Future<Response<ProfileListEnvelope>> listFollowers({
    required String id,
    String? cursor,
    int? limit = 20,
    CancelToken? cancelToken,
    Map<String, dynamic>? headers,
    Map<String, dynamic>? extra,
    ValidateStatus? validateStatus,
    ProgressCallback? onSendProgress,
    ProgressCallback? onReceiveProgress,
  }) => _held(api.followers, id, cursor);

  @override
  dynamic noSuchMethod(Invocation invocation) => super.noSuchMethod(invocation);
}
