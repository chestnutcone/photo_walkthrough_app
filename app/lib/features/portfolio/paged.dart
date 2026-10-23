import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/foundation.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_exception.dart';

const Object _keep = Object();

/// One keyset-paginated list: items so far, the cursor for the next page and
/// what went wrong, if anything.
@immutable
class PagedState<T> {
  const PagedState({
    this.items = const [],
    this.nextCursor,
    this.isLoading = false,
    this.loadingMore = false,
    this.error,
    this.moreError,
  });

  const PagedState.loading() : this(isLoading: true);

  final List<T> items;
  final String? nextCursor;

  /// First page in flight.
  final bool isLoading;
  final bool loadingMore;

  /// First page failed.
  final ApiException? error;

  /// A later page failed; [items] are kept.
  final ApiException? moreError;

  bool get canLoadMore =>
      nextCursor != null &&
      !isLoading &&
      !loadingMore &&
      error == null &&
      moreError == null;

  PagedState<T> copyWith({
    List<T>? items,
    Object? nextCursor = _keep,
    bool? loadingMore,
    Object? moreError = _keep,
  }) => PagedState(
    items: items ?? this.items,
    nextCursor: identical(nextCursor, _keep)
        ? this.nextCursor
        : nextCursor as String?,
    isLoading: isLoading,
    loadingMore: loadingMore ?? this.loadingMore,
    error: error,
    moreError: identical(moreError, _keep)
        ? this.moreError
        : moreError as ApiException?,
  );
}

typedef PageResult<T> = ({List<T> items, String? next});

/// Base for the portfolio's lists. Every (re)load, and every change of the
/// inputs watched in [watchInputs] (session epoch), starts a new generation
/// and cancels the previous request; a response from an older generation is
/// dropped even when the transport ignores cancellation.
abstract class PagedNotifier<T> extends Notifier<PagedState<T>> {
  int _generation = 0;
  CancelToken? _cancel;

  /// Watch everything the list depends on (session epoch).
  void watchInputs();

  Future<PageResult<T>> fetch(String? cursor, CancelToken token);

  String idOf(T item);

  @override
  PagedState<T> build() {
    watchInputs();
    _generation++;
    _cancel?.cancel();
    _cancel = null;
    ref.onDispose(() => _cancel?.cancel());
    scheduleMicrotask(() {
      if (ref.mounted) unawaited(reload());
    });
    return const PagedState.loading();
  }

  bool _isCurrent(int generation) => ref.mounted && generation == _generation;

  Future<void> reload() async {
    final generation = ++_generation;
    _cancel?.cancel();
    final token = _cancel = CancelToken();
    state = const PagedState.loading();
    try {
      final page = await fetch(null, token);
      if (!_isCurrent(generation)) return;
      state = PagedState(
        items: _merge(const [], page.items),
        nextCursor: page.next,
      );
    } on ApiException catch (e) {
      if (!_isCurrent(generation)) return;
      state = PagedState(error: e);
    }
  }

  Future<void> loadMore() async {
    final current = state;
    final cursor = current.nextCursor;
    final token = _cancel;
    if (!current.canLoadMore || cursor == null || token == null) return;
    final generation = _generation;
    state = current.copyWith(loadingMore: true);
    try {
      final page = await fetch(cursor, token);
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

  Future<void> retryMore() {
    state = state.copyWith(moreError: null);
    return loadMore();
  }

  List<T> _merge(List<T> existing, List<T> incoming) {
    final seen = {for (final i in existing) idOf(i)};
    return [
      ...existing,
      for (final i in incoming)
        if (seen.add(idOf(i))) i,
    ];
  }
}
