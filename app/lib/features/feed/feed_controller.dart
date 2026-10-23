import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/foundation.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:vela_api_client/vela_api_client.dart';

import '../../core/api/api_exception.dart';
import '../../core/api/api_providers.dart';

enum FeedTab { following, discover }

/// Changes on every sign-in and sign-out (including a direct account switch),
/// never on a token refresh. Feeds and follow state reset when it changes,
/// because every item's `isFollowing` depends on the viewer.
final feedViewerEpochProvider = Provider<int>(
  (ref) => ref.watch(sessionEpochProvider),
);

const Object _keep = Object();

@immutable
class FeedState {
  const FeedState({
    this.items = const [],
    this.nextCursor,
    this.isLoading = false,
    this.refreshing = false,
    this.loadingMore = false,
    this.error,
    this.moreError,
    this.refreshError,
  });

  const FeedState.loading() : this(isLoading: true);

  final List<Work> items;
  final String? nextCursor;

  /// First page in flight with nothing to show yet.
  final bool isLoading;

  /// Pull-to-refresh in flight; [items] stay visible meanwhile.
  final bool refreshing;
  final bool loadingMore;

  /// First page failed and there is nothing to show.
  final ApiException? error;

  /// A later page failed; [items] are kept.
  final ApiException? moreError;

  /// A refresh failed; [items] are kept.
  final ApiException? refreshError;

  bool get canLoadMore =>
      nextCursor != null &&
      !isLoading &&
      !refreshing &&
      !loadingMore &&
      moreError == null;

  FeedState copyWith({
    List<Work>? items,
    Object? nextCursor = _keep,
    bool? refreshing,
    bool? loadingMore,
    Object? moreError = _keep,
    Object? refreshError = _keep,
  }) => FeedState(
    items: items ?? this.items,
    nextCursor: identical(nextCursor, _keep)
        ? this.nextCursor
        : nextCursor as String?,
    isLoading: isLoading,
    refreshing: refreshing ?? this.refreshing,
    loadingMore: loadingMore ?? this.loadingMore,
    error: error,
    moreError: identical(moreError, _keep)
        ? this.moreError
        : moreError as ApiException?,
    refreshError: identical(refreshError, _keep)
        ? this.refreshError
        : refreshError as ApiException?,
  );
}

final feedControllerProvider = NotifierProvider.autoDispose
    .family<FeedController, FeedState, FeedTab>(FeedController.new);

typedef _Page = ({List<Work> items, String? next});

/// Keyset-paginated feed for one tab.
///
/// Every (re)load starts a new generation and cancels the previous requests;
/// a response whose generation is no longer current is dropped.
class FeedController extends Notifier<FeedState> {
  FeedController(this.tab);

  final FeedTab tab;
  int _generation = 0;
  CancelToken? _cancel;

  @override
  FeedState build() {
    ref.watch(feedViewerEpochProvider);
    _generation++;
    _cancel?.cancel();
    _cancel = null;
    ref.onDispose(() => _cancel?.cancel());
    scheduleMicrotask(() {
      if (ref.mounted) unawaited(_loadFirst(keepItems: false));
    });
    return const FeedState.loading();
  }

  bool _isCurrent(int generation) => ref.mounted && generation == _generation;

  /// Reloads from the first page (retry, and pull-to-refresh).
  Future<void> refresh() {
    // A loaded feed (even an empty one) stays on screen while it reloads.
    final loaded = !state.isLoading && state.error == null;
    return _loadFirst(keepItems: loaded);
  }

  Future<void> _loadFirst({required bool keepItems}) async {
    final generation = ++_generation;
    _cancel?.cancel();
    final token = _cancel = CancelToken();
    state = keepItems
        ? state.copyWith(
            refreshing: true,
            loadingMore: false,
            refreshError: null,
          )
        : const FeedState.loading();
    try {
      final page = await _fetch(null, token);
      if (!_isCurrent(generation)) return;
      state = FeedState(
        items: _merge(const [], page.items),
        nextCursor: page.next,
      );
    } on ApiException catch (e) {
      if (!_isCurrent(generation)) return;
      state = keepItems
          ? state.copyWith(refreshing: false, refreshError: e)
          : FeedState(error: e);
    }
  }

  /// Loads the next page. No-op unless one is available and idle.
  Future<void> loadMore() async {
    final current = state;
    final cursor = current.nextCursor;
    final token = _cancel;
    if (!current.canLoadMore || cursor == null || token == null) return;
    final generation = _generation;
    state = current.copyWith(loadingMore: true);
    try {
      final page = await _fetch(cursor, token);
      if (!_isCurrent(generation)) return;
      state = state.copyWith(
        items: _merge(state.items, page.items),
        nextCursor: page.next,
        loadingMore: false,
      );
    } on ApiException catch (e) {
      if (!_isCurrent(generation)) return;
      state = state.copyWith(loadingMore: false, moreError: e);
    }
  }

  /// Retries a failed later page.
  Future<void> retryMore() {
    state = state.copyWith(moreError: null);
    return loadMore();
  }

  Future<_Page> _fetch(String? cursor, CancelToken token) async {
    final env = (await apiGuard(
      () => ref
          .read(apiProvider)
          .getWorksApi()
          .getFeed(tab: tab.name, cursor: cursor, cancelToken: token),
    )).data;
    final items = unwrapEnvelope(
      success: env?.success ?? false,
      data: env?.data,
      error: env?.error,
    );
    return (items: items, next: env?.page?.nextCursor);
  }

  static List<Work> _merge(List<Work> existing, List<Work> incoming) {
    final seen = {for (final w in existing) w.id};
    return [
      ...existing,
      for (final w in incoming)
        if (seen.add(w.id)) w,
    ];
  }
}
