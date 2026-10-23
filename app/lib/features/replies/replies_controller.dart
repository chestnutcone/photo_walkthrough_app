import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/foundation.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:vela_api_client/vela_api_client.dart';

import '../../core/api/api_exception.dart';
import '../../core/api/api_providers.dart';

/// Server limit, counted in Unicode code points (not UTF-16 units).
const int kReplyMaxLength = 1000;

int codePointCount(String s) => s.runes.length;

typedef RepliesKey = ({String workId, int chapterIndex});

enum SendOutcome { sent, failed, ignored }

const Object _keep = Object();

@immutable
class RepliesState {
  const RepliesState({
    this.items = const [],
    this.nextCursor,
    this.isLoading = false,
    this.loadingMore = false,
    this.sending = false,
    this.error,
    this.moreError,
    this.sendError,
    this.actionError,
    this.ownIds = const {},
  });

  const RepliesState.loading() : this(isLoading: true);

  /// Oldest first, as served.
  final List<Reply> items;
  final String? nextCursor;
  final bool isLoading;
  final bool loadingMore;
  final bool sending;

  /// First page failed and there is nothing to show.
  final ApiException? error;
  final ApiException? moreError;
  final ApiException? sendError;

  /// A delete failed and was rolled back.
  final ApiException? actionError;

  /// Replies this viewer sent in this session of the sheet.
  final Set<String> ownIds;

  bool get canLoadMore =>
      nextCursor != null && !isLoading && !loadingMore && moreError == null;

  RepliesState copyWith({
    List<Reply>? items,
    Object? nextCursor = _keep,
    bool? loadingMore,
    bool? sending,
    Object? moreError = _keep,
    Object? sendError = _keep,
    Object? actionError = _keep,
  }) => RepliesState(
    items: items ?? this.items,
    nextCursor: identical(nextCursor, _keep)
        ? this.nextCursor
        : nextCursor as String?,
    isLoading: isLoading,
    loadingMore: loadingMore ?? this.loadingMore,
    sending: sending ?? this.sending,
    error: error,
    moreError: identical(moreError, _keep)
        ? this.moreError
        : moreError as ApiException?,
    sendError: identical(sendError, _keep)
        ? this.sendError
        : sendError as ApiException?,
    actionError: identical(actionError, _keep)
        ? this.actionError
        : actionError as ApiException?,
    ownIds: ownIds,
  );
}

final repliesControllerProvider = NotifierProvider.autoDispose
    .family<RepliesController, RepliesState, RepliesKey>(RepliesController.new);

/// Ids that identify the signed-in viewer in reply authorship (the account id
/// and its profile id). Empty for guests or when `/me` fails.
final viewerIdsProvider = FutureProvider.autoDispose<Set<String>>((ref) async {
  ref.watch(sessionEpochProvider);
  final signedIn = ref.watch(sessionProvider.select((s) => s.value != null));
  if (!signedIn) return const {};
  try {
    final env = (await apiGuard(() => ref.read(apiProvider).getMeApi().getMe()))
        .data;
    final me = env?.data;
    if (me == null) return const {};
    return {me.id, ?me.profile?.id};
  } on Object {
    // Unknown viewer: no reply is treated as own, so nothing can be deleted.
    return const {};
  }
});

typedef _Page = ({List<Reply> items, String? next});

/// Keyset-paginated replies for one chapter.
///
/// `_generation` changes on every (re)load and on session change; `_epoch`
/// only on session change. A response for an older value is dropped.
class RepliesController extends Notifier<RepliesState> {
  RepliesController(this.key);

  final RepliesKey key;
  int _generation = 0;
  int _epoch = 0;
  CancelToken? _cancel;

  /// Replies sent since the last (re)build; kept when a first page that was
  /// requested before the send arrives without them.
  final List<Reply> _sent = [];
  final Set<String> _own = {};

  @override
  RepliesState build() {
    ref.watch(sessionEpochProvider);
    _epoch++;
    _generation++;
    _sent.clear();
    _own.clear();
    _cancel?.cancel();
    _cancel = null;
    ref.onDispose(() => _cancel?.cancel());
    scheduleMicrotask(() {
      if (ref.mounted) unawaited(_loadFirst(keepItems: false));
    });
    return const RepliesState.loading();
  }

  bool _isCurrent(int generation) => ref.mounted && generation == _generation;
  bool _isEpoch(int epoch) => ref.mounted && epoch == _epoch;

  Future<void> refresh() =>
      _loadFirst(keepItems: !state.isLoading && state.error == null);

  Future<void> _loadFirst({required bool keepItems}) async {
    final generation = ++_generation;
    _cancel?.cancel();
    final token = _cancel = CancelToken();
    // The old cursor must not be usable while the first page reloads.
    final oldCursor = state.nextCursor;
    state = keepItems
        ? state.copyWith(loadingMore: false, moreError: null, nextCursor: null)
        : const RepliesState.loading();
    try {
      final page = await _fetch(null, token);
      if (!_isCurrent(generation)) return;
      state = RepliesState(
        items: _merge(page.items, _sent),
        nextCursor: page.next,
        ownIds: Set.of(_own),
      );
    } on ApiException catch (e) {
      if (!_isCurrent(generation)) return;
      state = keepItems
          ? state.copyWith(moreError: e, nextCursor: oldCursor)
          : RepliesState(error: e);
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

  Future<void> retryMore() {
    state = state.copyWith(moreError: null);
    return loadMore();
  }

  /// Posts [raw] (trimmed). The server's reply is appended on success.
  Future<SendOutcome> send(String raw) async {
    final text = raw.trim();
    if (state.sending ||
        text.isEmpty ||
        codePointCount(text) > kReplyMaxLength) {
      return SendOutcome.ignored;
    }
    final epoch = _epoch;
    state = state.copyWith(sending: true, sendError: null);
    try {
      final env = (await apiGuard(
        () => ref
            .read(apiProvider)
            .getRepliesApi()
            .addReply(
              id: key.workId,
              index: key.chapterIndex,
              addReplyRequest: AddReplyRequest(body: text),
            ),
      )).data;
      final created = unwrapEnvelope(
        success: env?.success ?? false,
        data: env?.data,
        error: env?.error,
      );
      // The session changed mid-flight: the new state belongs to someone else.
      if (!_isEpoch(epoch)) return SendOutcome.sent;
      _sent.add(created);
      _own.add(created.id);
      state = RepliesState(
        items: _merge(state.items, [created]),
        nextCursor: state.nextCursor,
        loadingMore: state.loadingMore,
        moreError: state.moreError,
        actionError: state.actionError,
        ownIds: Set.of(_own),
      );
      return SendOutcome.sent;
    } on ApiException catch (e) {
      if (!_isEpoch(epoch)) return SendOutcome.failed;
      state = state.copyWith(sending: false, sendError: e);
      return SendOutcome.failed;
    }
  }

  /// Removes [id] at once and restores it if the server refuses.
  Future<bool> delete(String id) async {
    final index = state.items.indexWhere((r) => r.id == id);
    if (index < 0) return false;
    final removed = state.items[index];
    final epoch = _epoch;
    state = state.copyWith(
      items: [...state.items]..removeAt(index),
      actionError: null,
    );
    final wasSent = _sent.any((r) => r.id == id);
    _sent.removeWhere((r) => r.id == id);
    try {
      final env = (await apiGuard(
        () => ref.read(apiProvider).getRepliesApi().deleteReply(id: id),
      )).data;
      if (!(env?.success ?? false) && env?.error?.code != 'not_found') {
        throw ApiException(
          code: env?.error?.code ?? 'unknown_error',
          message: env?.error?.message ?? '',
        );
      }
      return true;
    } on ApiException catch (e) {
      if (e.code == 'not_found') return true; // already gone
      if (!_isEpoch(epoch)) return false;
      if (state.items.any((r) => r.id == id)) return false;
      if (wasSent) _sent.add(removed);
      final at = index.clamp(0, state.items.length);
      state = state.copyWith(
        items: [...state.items]..insert(at, removed),
        actionError: e,
      );
      return false;
    }
  }

  /// Hides a reply after the viewer reported it.
  void hideLocally(String id) {
    _sent.removeWhere((r) => r.id == id);
    state = state.copyWith(
      items: [
        for (final r in state.items)
          if (r.id != id) r,
      ],
    );
  }

  void clearSendError() => state = state.copyWith(sendError: null);
  void clearActionError() => state = state.copyWith(actionError: null);

  Future<_Page> _fetch(String? cursor, CancelToken token) async {
    final env = (await apiGuard(
      () => ref
          .read(apiProvider)
          .getRepliesApi()
          .listReplies(
            id: key.workId,
            index: key.chapterIndex,
            cursor: cursor,
            cancelToken: token,
          ),
    )).data;
    final items = unwrapEnvelope(
      success: env?.success ?? false,
      data: env?.data,
      error: env?.error,
    );
    return (items: items, next: env?.page?.nextCursor);
  }

  static List<Reply> _merge(List<Reply> existing, List<Reply> incoming) {
    final seen = {for (final r in existing) r.id};
    return [
      ...existing,
      for (final r in incoming)
        if (seen.add(r.id)) r,
    ];
  }
}

/// User-facing text for a failed send or delete. Never includes reply text.
String describeReplyError(ApiException e) {
  if (e.code == 'rate_limited') {
    final wait = e.retryAfter;
    if (wait == null) return 'You are replying too fast. Try again shortly.';
    final s = wait.inSeconds < 1 ? 1 : wait.inSeconds;
    return 'You are replying too fast. Try again in $s '
        '${s == 1 ? 'second' : 'seconds'}.';
  }
  if (e.code == 'network_error') {
    return 'No connection. Check your network and try again.';
  }
  return 'Something went wrong. Please try again.';
}
