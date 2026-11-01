import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/foundation.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:vela_api_client/vela_api_client.dart';

import '../../core/api/api_exception.dart';
import '../../core/api/api_providers.dart';
import '../portfolio/paged.dart';
import 'recent_searches.dart';
import 'search_rules.dart';

enum SearchTab {
  artists('artists'),
  stories('stories');

  const SearchTab(this.apiValue);
  final String apiValue;
}

/// One search result row, either kind.
sealed class SearchHit {
  const SearchHit();
  String get id;
}

class ArtistHit extends SearchHit {
  const ArtistHit(this.profile);
  final Profile profile;
  @override
  String get id => profile.id;
}

class StoryHit extends SearchHit {
  const StoryHit(this.work);
  final Work work;
  @override
  String get id => work.id;
}

const Object _keep = Object();

@immutable
class SearchState {
  const SearchState({
    this.input = '',
    this.query,
    this.tab = SearchTab.artists,
    this.tooLong = false,
    this.artists,
    this.stories,
  });

  /// What is in the box (untrimmed).
  final String input;

  /// The trimmed text the results belong to; null before a valid search.
  final String? query;
  final SearchTab tab;

  /// The text is over the length the server accepts; nothing is sent.
  final bool tooLong;

  /// Results per tab for [query]; null = not requested yet.
  final PagedState<SearchHit>? artists;
  final PagedState<SearchHit>? stories;

  PagedState<SearchHit>? slice(SearchTab t) =>
      t == SearchTab.artists ? artists : stories;

  PagedState<SearchHit>? get current => slice(tab);

  SearchState copyWith({
    String? input,
    Object? query = _keep,
    SearchTab? tab,
    bool? tooLong,
    Object? artists = _keep,
    Object? stories = _keep,
  }) => SearchState(
    input: input ?? this.input,
    query: identical(query, _keep) ? this.query : query as String?,
    tab: tab ?? this.tab,
    tooLong: tooLong ?? this.tooLong,
    artists: identical(artists, _keep)
        ? this.artists
        : artists as PagedState<SearchHit>?,
    stories: identical(stories, _keep)
        ? this.stories
        : stories as PagedState<SearchHit>?,
  );

  SearchState withSlice(SearchTab t, PagedState<SearchHit>? value) =>
      t == SearchTab.artists
      ? copyWith(artists: value)
      : copyWith(stories: value);
}

/// Artists worth following, for the empty state. Public endpoint, so guests
/// get it too; reloads when the viewer changes because `isFollowing` does.
final suggestedArtistsProvider = FutureProvider.autoDispose<List<Profile>>((
  ref,
) async {
  ref.watch(sessionEpochProvider);
  final env = (await apiGuard(
    () => ref.read(apiProvider).getArtistsApi().suggestedArtists(),
  )).data;
  return unwrapEnvelope(
    success: env?.success ?? false,
    data: env?.data,
    error: env?.error,
  );
}, retry: (_, _) => null);

final searchControllerProvider =
    NotifierProvider.autoDispose<SearchResultsController, SearchState>(
      SearchResultsController.new,
    );

/// Search state machine. Privacy: the query is never logged, printed or
/// handed to telemetry here.
///
/// Stale answers are dropped by a counter, not by cancellation (a transport
/// may ignore it): [_round] changes whenever the query or viewer changes.
/// Within one round a tab never has two requests running (a tab loads only
/// when empty, retries only after an error, pages only when idle).
class SearchResultsController extends Notifier<SearchState> {
  Timer? _debounce;
  int _round = 0;
  final _tokens = <SearchTab, CancelToken>{};

  @override
  SearchState build() {
    ref.listen(sessionEpochProvider, (_, _) {
      final q = state.query;
      if (q != null) _commit(q);
    });
    ref.onDispose(() {
      _debounce?.cancel();
      _cancelAll();
    });
    return const SearchState();
  }

  void _cancelAll() {
    for (final t in _tokens.values) {
      t.cancel();
    }
    _tokens.clear();
  }

  /// The text box changed. Searches [searchDebounce] after the last change.
  void setInput(String text) {
    _debounce?.cancel();
    final q = cleanQuery(text);
    if (!isSearchableQuery(q)) {
      _round++;
      _cancelAll();
      state = state.copyWith(
        input: text,
        query: null,
        artists: null,
        stories: null,
        tooLong: q.length > maxSearchLength,
      );
      return;
    }
    state = state.copyWith(input: text, tooLong: false);
    if (q == state.query) return;
    _debounce = Timer(searchDebounce, () {
      if (ref.mounted) _commit(q);
    });
  }

  /// Keyboard "search": searches now and remembers the text.
  void submit() {
    _debounce?.cancel();
    final q = cleanQuery(state.input);
    if (!isSearchableQuery(q)) return;
    if (q != state.query) _commit(q);
    rememberQuery();
  }

  /// Adds the current query to the recent searches (on submit or when a
  /// result is opened; never while merely typing).
  void rememberQuery() {
    final q = state.query;
    if (q != null) unawaited(ref.read(recentSearchesProvider.notifier).add(q));
  }

  void selectTab(SearchTab tab) {
    if (tab == state.tab) return;
    state = state.copyWith(tab: tab);
    if (state.query != null && state.slice(tab) == null) _load(tab);
  }

  void retry() {
    final tab = state.tab;
    if (state.query != null && state.slice(tab)?.error != null) _load(tab);
  }

  void loadMore() {
    final tab = state.tab;
    final slice = state.slice(tab);
    final cursor = slice?.nextCursor;
    if (slice == null || cursor == null || !slice.canLoadMore) return;
    _load(tab, cursor: cursor, from: slice);
  }

  void retryMore() {
    final tab = state.tab;
    final slice = state.slice(tab);
    if (slice == null || slice.moreError == null) return;
    state = state.withSlice(tab, slice.copyWith(moreError: null));
    loadMore();
  }

  void _commit(String q) {
    _cancelAll();
    _round++;
    state = state.copyWith(query: q, artists: null, stories: null);
    _load(state.tab);
  }

  Future<void> _load(
    SearchTab tab, {
    String? cursor,
    PagedState<SearchHit>? from,
  }) async {
    final q = state.query;
    if (q == null) return;
    final round = _round;
    _tokens[tab]?.cancel();
    final token = _tokens[tab] = CancelToken();
    bool current() => ref.mounted && _round == round;

    state = state.withSlice(
      tab,
      from == null
          ? const PagedState.loading()
          : from.copyWith(loadingMore: true),
    );
    try {
      final page = await _fetch(tab, q, cursor, token);
      if (!current()) return;
      final seen = {for (final h in from?.items ?? const <SearchHit>[]) h.id};
      state = state.withSlice(
        tab,
        PagedState<SearchHit>(
          items: [
            ...?from?.items,
            for (final h in page.items)
              if (seen.add(h.id)) h,
          ],
          nextCursor: page.next,
        ),
      );
    } on ApiException catch (e) {
      if (!current()) return;
      state = state.withSlice(
        tab,
        from == null
            ? PagedState<SearchHit>(error: e)
            : from.copyWith(loadingMore: false, moreError: e),
      );
    }
  }

  Future<({List<SearchHit> items, String? next})> _fetch(
    SearchTab tab,
    String q,
    String? cursor,
    CancelToken token,
  ) async {
    final env = (await apiGuard(
      () => ref
          .read(apiProvider)
          .getSearchApi()
          .search(q: q, type: tab.apiValue, cursor: cursor, cancelToken: token),
    )).data;
    final data = unwrapEnvelope(
      success: env?.success ?? false,
      data: env?.data,
      error: env?.error,
    );
    final items = tab == SearchTab.artists
        ? <SearchHit>[for (final p in data.artists) ArtistHit(p)]
        : <SearchHit>[for (final w in data.stories) StoryHit(w)];
    return (items: items, next: env?.page?.nextCursor);
  }
}
