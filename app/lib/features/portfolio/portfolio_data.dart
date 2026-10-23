import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:vela_api_client/vela_api_client.dart';

import '../../core/api/api_exception.dart';
import '../../core/api/api_providers.dart';
import 'paged.dart';

/// One artist, by handle. Reloads when the session changes because
/// `isFollowing` depends on the viewer. Failures are not retried
/// automatically; the screen offers an explicit retry.
final artistProvider = FutureProvider.autoDispose.family<Profile, String>((
  ref,
  handle,
) async {
  ref.watch(sessionEpochProvider);
  final res = await apiGuard(
    () => ref.watch(apiProvider).getArtistsApi().getArtist(handle: handle),
  );
  final env = res.data;
  return unwrapEnvelope(
    success: env?.success ?? false,
    data: env?.data,
    error: env?.error,
  );
}, retry: (_, _) => null);

/// The signed-in viewer (null for guests). Carries the quota
/// (`usedWorks` of `tier.maxWorks`) and the viewer's own handle and ids.
/// A failed lookup is an error state, never "not me"; see [viewerStatusProvider].
final meProvider = FutureProvider.autoDispose<Me?>((ref) async {
  ref.watch(sessionEpochProvider);
  final signedIn = ref.watch(sessionProvider.select((s) => s.value != null));
  if (!signedIn) return null;
  final env = (await apiGuard(() => ref.read(apiProvider).getMeApi().getMe()))
      .data;
  return env?.data;
}, retry: (_, _) => null);

/// Whether the viewer is known well enough to offer Follow, Contact and
/// Report. Until a signed-in viewer's `/me` resolves we cannot tell whether
/// this is their own profile.
enum ViewerStatus { loading, failed, ready }

final viewerStatusProvider = Provider.autoDispose<ViewerStatus>((ref) {
  final signedIn = ref.watch(sessionProvider.select((s) => s.value != null));
  if (!signedIn) return ViewerStatus.ready;
  final me = ref.watch(meProvider);
  if (me.isLoading) return ViewerStatus.loading;
  return me.hasError ? ViewerStatus.failed : ViewerStatus.ready;
});

/// The image shown for a work in lists and covers.
String? workImageUrl(Work w) => w.feedUrl ?? w.thumbUrl ?? w.imageUrl;

/// Whether [profile] is the signed-in viewer.
bool isOwnProfile(Me? me, Profile profile) =>
    me != null && (me.profile?.id == profile.id);

/// Missing, blocked and suspended artists all look the same to the viewer,
/// so existence is not leaked. Everything else (network, 429, 5xx) is a
/// retryable failure.
bool isArtistUnavailable(Object error) =>
    error is ApiException &&
    (const {401, 403, 404, 410}.contains(error.statusCode) ||
        error.code == 'not_found');

/// Works of one artist, newest first.
final worksControllerProvider = NotifierProvider.autoDispose
    .family<WorksController, PagedState<Work>, String>(WorksController.new);

class WorksController extends PagedNotifier<Work> {
  WorksController(this.handle);
  final String handle;

  @override
  void watchInputs() => ref.watch(sessionEpochProvider);

  @override
  String idOf(Work item) => item.id;

  @override
  Future<PageResult<Work>> fetch(String? cursor, CancelToken token) async {
    final env = (await apiGuard(
      () => ref
          .read(apiProvider)
          .getArtistsApi()
          .listArtistWorks(handle: handle, cursor: cursor, cancelToken: token),
    )).data;
    final items = unwrapEnvelope(
      success: env?.success ?? false,
      data: env?.data,
      error: env?.error,
    );
    return (items: items, next: env?.page?.nextCursor);
  }
}

/// Followers of one artist, by artist id.
final followersControllerProvider = NotifierProvider.autoDispose
    .family<FollowersController, PagedState<Profile>, String>(
      FollowersController.new,
    );

class FollowersController extends PagedNotifier<Profile> {
  FollowersController(this.artistId);
  final String artistId;

  @override
  void watchInputs() => ref.watch(sessionEpochProvider);

  @override
  String idOf(Profile item) => item.id;

  @override
  Future<PageResult<Profile>> fetch(String? cursor, CancelToken token) async {
    final env = (await apiGuard(
      () => ref
          .read(apiProvider)
          .getArtistsApi()
          .listFollowers(id: artistId, cursor: cursor, cancelToken: token),
    )).data;
    final items = unwrapEnvelope(
      success: env?.success ?? false,
      data: env?.data,
      error: env?.error,
    );
    return (items: items, next: env?.page?.nextCursor);
  }
}
