import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/foundation.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:vela_api_client/vela_api_client.dart' as api;

import '../../core/api/api_exception.dart';
import '../../core/api/api_providers.dart';
import 'notification_item.dart';
import 'unread_count_provider.dart';

const Object _keep = Object();

@immutable
class NotificationsState {
  const NotificationsState({
    this.items = const [],
    this.nextCursor,
    this.isLoading = false,
    this.refreshing = false,
    this.loadingMore = false,
    this.error,
    this.moreError,
    this.refreshError,
    this.skippedRows = 0,
  });

  const NotificationsState.loading() : this(isLoading: true);

  final List<NotificationItem> items;
  final String? nextCursor;

  /// First page in flight with nothing to show.
  final bool isLoading;

  /// A refresh is in flight; [items] stay visible.
  final bool refreshing;
  final bool loadingMore;

  /// The first page failed and there is nothing to show.
  final ApiException? error;

  /// A later page failed; [items] are kept.
  final ApiException? moreError;

  /// A refresh failed; [items] are kept.
  final ApiException? refreshError;

  /// Rows in the pages so far that could not be read and were left out.
  final int skippedRows;

  bool get canLoadMore =>
      nextCursor != null &&
      !isLoading &&
      !refreshing &&
      !loadingMore &&
      error == null &&
      moreError == null;

  NotificationsState copyWith({
    List<NotificationItem>? items,
    Object? nextCursor = _keep,
    bool? refreshing,
    bool? loadingMore,
    Object? moreError = _keep,
    Object? refreshError = _keep,
    int? skippedRows,
  }) => NotificationsState(
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
    skippedRows: skippedRows ?? this.skippedRows,
  );
}

typedef _Page = ({List<NotificationItem> items, String? next, int skipped});

/// Fetches one raw page of `GET /v1/notifications`. A seam: tests replace it
/// with a fake that ignores the cancel token.
typedef NotificationsFetcher = Future<Object?> Function(
  String? cursor,
  CancelToken token,
);

/// The app reads this endpoint itself (not through the generated client,
/// whose closed `type` enum would fail a whole page over one type this
/// version does not know) and parses every row tolerantly.
final notificationsFetcherProvider = Provider<NotificationsFetcher>((ref) {
  return (cursor, token) async =>
      (await ref
              .read(dioProvider)
              .get<Object>(
                '/v1/notifications',
                queryParameters: {'cursor': ?cursor},
                cancelToken: token,
              ))
          .data;
});

/// The signed-in viewer's notifications, newest first. Per user: everything
/// resets when the session epoch changes.
///
/// Stale answers are dropped with a generation number, not cancellation (a
/// transport may ignore it): every first load and refresh starts a new
/// generation, and a page request only applies to the generation it began in.
final notificationsControllerProvider =
    NotifierProvider.autoDispose<NotificationsController, NotificationsState>(
      NotificationsController.new,
    );

class NotificationsController extends Notifier<NotificationsState> {
  int _generation = 0;
  CancelToken? _cancel;

  /// Item ids the viewer has read here whose server write has not finished.
  /// A page that arrives meanwhile still shows them read.
  Set<String> _pendingRead = {};

  @override
  NotificationsState build() {
    ref.watch(sessionEpochProvider);
    _generation++;
    _pendingRead = {};
    _cancel?.cancel();
    _cancel = null;
    ref.onDispose(() => _cancel?.cancel());
    scheduleMicrotask(() {
      if (ref.mounted) unawaited(_loadFirst());
    });
    return const NotificationsState.loading();
  }

  bool _isCurrent(int generation) => ref.mounted && generation == _generation;

  CancelToken _newRequest() {
    _cancel?.cancel();
    return _cancel = CancelToken();
  }

  Future<bool> _signedIn() async =>
      (await ref.read(sessionProvider.future)) != null;

  /// First load, and the retry after it failed.
  Future<void> _loadFirst() async {
    final generation = ++_generation;
    final token = _newRequest();
    state = const NotificationsState.loading();
    try {
      if (!await _signedIn()) {
        if (_isCurrent(generation)) state = const NotificationsState();
        return;
      }
      final page = await _fetch(null, token);
      if (!_isCurrent(generation)) return;
      state = NotificationsState(
        items: _applyPending(page.items),
        nextCursor: page.next,
        skippedRows: page.skipped,
      );
    } on ApiException catch (e) {
      if (!_isCurrent(generation)) return;
      state = NotificationsState(error: e);
    }
  }

  Future<void> retry() => _loadFirst();

  /// Reloads the first page, keeping the list on screen meanwhile.
  Future<void> refresh() async {
    if (state.isLoading || state.error != null) return _loadFirst();
    final generation = ++_generation;
    final token = _newRequest();
    state = state.copyWith(
      refreshing: true,
      loadingMore: false,
      refreshError: null,
      moreError: null,
    );
    try {
      final page = await _fetch(null, token);
      if (!_isCurrent(generation)) return;
      state = NotificationsState(
        items: _applyPending(page.items),
        nextCursor: page.next,
        skippedRows: page.skipped,
      );
    } on ApiException catch (e) {
      if (!_isCurrent(generation)) return;
      state = state.copyWith(refreshing: false, refreshError: e);
    }
  }

  Future<void> loadMore() async {
    final cursor = state.nextCursor;
    final token = _cancel;
    if (!state.canLoadMore || cursor == null || token == null) return;
    final generation = _generation;
    state = state.copyWith(loadingMore: true);
    try {
      final page = await _fetch(cursor, token);
      if (!_isCurrent(generation)) return;
      final seen = {for (final i in state.items) i.id};
      state = state.copyWith(
        items: [
          ...state.items,
          for (final i in _applyPending(page.items))
            if (seen.add(i.id)) i,
        ],
        nextCursor: page.next,
        loadingMore: false,
        skippedRows: state.skippedRows + page.skipped,
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

  /// Marks [item] (and every row folded into it) read: shown at once, undone
  /// if the server refuses. True when the server accepted. The badge is
  /// refetched either way.
  Future<bool> markRead(NotificationItem item) async {
    if (item.read) return true;
    final epoch = ref.read(sessionEpochProvider);
    // Outlives a screen that is closed before the answer arrives.
    final keepAlive = ref.keepAlive();
    final container = ref.container;
    _pendingRead.add(item.id);
    state = state.copyWith(items: _withRead(item.id, true));
    var accepted = false;
    try {
      final env = (await apiGuard(
        () => ref
            .read(apiProvider)
            .getNotificationsApi()
            .markRead(markReadRequest: api.MarkReadRequest(ids: item.ids)),
      )).data;
      unwrapEnvelope<bool>(
        success: env?.success ?? false,
        data: true,
        error: env?.error,
      );
      accepted = true;
    } on ApiException {
      // Undone below, for the session that made the change only.
    } finally {
      if (ref.mounted) {
        if (epoch == ref.read(sessionEpochProvider)) {
          _pendingRead.remove(item.id);
          if (!accepted) {
            state = state.copyWith(items: _withRead(item.id, false));
          }
        }
        container.invalidate(unreadCountProvider);
        keepAlive.close();
      }
    }
    return accepted;
  }

  List<NotificationItem> _withRead(String id, bool read) => [
    for (final i in state.items) i.id == id ? i.copyWith(read: read) : i,
  ];

  List<NotificationItem> _applyPending(List<NotificationItem> items) =>
      _pendingRead.isEmpty
      ? items
      : [
          for (final i in items)
            _pendingRead.contains(i.id) ? i.copyWith(read: true) : i,
        ];

  /// One page, parsed tolerantly: an unknown row type is generic text, an
  /// unreadable row is skipped (counted, never logged with content), and a
  /// body that is not a list is an error rather than an empty inbox.
  Future<_Page> _fetch(String? cursor, CancelToken token) async {
    final body = await apiGuard(
      () => ref.read(notificationsFetcherProvider)(cursor, token),
    );
    if (body is! Map) throw _malformed;
    if (body['success'] != true || body['error'] != null) {
      final error = body['error'];
      throw ApiException(
        code: error is Map && error['code'] is String
            ? error['code'] as String
            : 'unknown_error',
        message: error is Map && error['message'] is String
            ? error['message'] as String
            : '',
      );
    }
    final data = body['data'];
    if (data is! List) throw _malformed;
    final parsed = parseNotificationRows(data);
    if (parsed.skipped > 0) {
      // Count only: row content can be personal.
      debugPrint('notifications: skipped ${parsed.skipped} unreadable rows');
    }
    final page = body['page'];
    final next = page is Map ? page['nextCursor'] : null;
    return (
      items: parsed.items,
      next: next is String ? next : null,
      skipped: parsed.skipped,
    );
  }
}

const _malformed = ApiException(
  code: 'malformed_response',
  message: 'Response was not a notification list',
);
