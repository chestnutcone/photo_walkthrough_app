import 'package:fake_async/fake_async.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:vela_app/core/api/api_providers.dart';
import 'package:vela_app/core/auth/session_store.dart';
import 'package:vela_app/features/search/recent_searches.dart';
import 'package:vela_app/features/search/search_controller.dart';

import '../../core/support/fakes.dart';
import 'support/fake_search_api.dart';
import 'support/memory_recents.dart';

const _debounce = Duration(milliseconds: 300);

class _Rig {
  _Rig() {
    container = ProviderContainer(
      overrides: [
        apiProvider.overrideWithValue(api),
        sessionStorageProvider.overrideWithValue(FakeSessionStorage()),
        recentSearchesStoreProvider.overrideWithValue(MemoryRecentsStore()),
      ],
    );
    container.listen(searchControllerProvider, (_, _) {});
  }

  final api = FakeSearchApi();
  late final ProviderContainer container;

  SearchResultsController get ctl =>
      container.read(searchControllerProvider.notifier);
  SearchState get state => container.read(searchControllerProvider);
  List<String> get artistIds => [
    for (final p in state.artists?.items ?? const []) p.id as String,
  ];
  List<String> get storyIds => [
    for (final w in state.stories?.items ?? const []) w.id as String,
  ];
}

void _run(String name, void Function(FakeAsync async, _Rig rig) body) {
  test(name, () {
    fakeAsync((async) {
      final rig = _Rig();
      addTearDown(rig.container.dispose);
      body(async, rig);
    });
  });
}

void main() {
  _run('no_request_below_two_characters', (async, rig) {
    rig.ctl.setInput('a');
    async.elapse(const Duration(seconds: 2));
    rig.ctl.setInput('  b  ');
    async.elapse(const Duration(seconds: 2));
    rig.ctl.setInput('');
    rig.ctl.submit();
    async.elapse(const Duration(seconds: 2));
    expect(rig.api.searchCalls, isEmpty);
    expect(rig.state.query, isNull);
  });

  _run('submit_below_two_characters_sends_nothing', (async, rig) {
    rig.ctl.setInput(' x ');
    rig.ctl.submit();
    async.flushMicrotasks();
    expect(rig.api.searchCalls, isEmpty);
  });

  _run('shortening_the_text_drops_results_and_ignores_the_late_answer', (
    async,
    rig,
  ) {
    rig.ctl.setInput('cam');
    async.elapse(_debounce);
    final call = rig.api.searchCalls.single;
    rig.ctl.setInput('c');
    call.reply(artists: ['a1']);
    async.flushMicrotasks();
    expect(rig.state.query, isNull);
    expect(rig.state.artists, isNull);
  });

  _run('debounce_collapses_rapid_typing_to_one_request', (async, rig) {
    rig.ctl.setInput('ca');
    async.elapse(const Duration(milliseconds: 100));
    rig.ctl.setInput('cam');
    async.elapse(const Duration(milliseconds: 100));
    rig.ctl.setInput('came');
    async.elapse(const Duration(milliseconds: 299));
    expect(rig.api.searchCalls, isEmpty);
    async.elapse(const Duration(milliseconds: 1));
    expect(rig.api.searchCalls, hasLength(1));
    expect(rig.api.searchCalls.single.q, 'came');
    expect(rig.api.searchCalls.single.type, 'artists');
  });

  _run('the_query_is_trimmed_before_it_is_sent', (async, rig) {
    rig.ctl.setInput('  lisbon  ');
    async.elapse(_debounce);
    expect(rig.api.searchCalls.single.q, 'lisbon');
  });

  _run('typing_trailing_spaces_does_not_repeat_the_same_search', (async, rig) {
    rig.ctl.setInput('lisbon');
    async.elapse(_debounce);
    rig.api.searchCalls.single.reply(artists: ['a1']);
    async.flushMicrotasks();
    rig.ctl.setInput('lisbon ');
    async.elapse(_debounce);
    expect(rig.api.searchCalls, hasLength(1));
    expect(rig.artistIds, ['a1']);
  });

  _run('control_characters_are_stripped_before_sending', (async, rig) {
    rig.ctl.setInput('sa\u0000l\u0007t\u007f');
    async.elapse(_debounce);
    expect(rig.api.searchCalls.single.q, 'salt');
  });

  _run('a_query_of_only_control_characters_is_not_sent', (async, rig) {
    rig.ctl.setInput('\u0000\u0000\u0001');
    async.elapse(_debounce);
    rig.ctl.submit();
    expect(rig.api.searchCalls, isEmpty);
    expect(rig.state.query, isNull);
  });

  _run('words_split_by_tabs_and_newlines_stay_split', (async, rig) {
    rig.ctl.setInput('lisbon\tsalt');
    async.elapse(_debounce);
    expect(rig.api.searchCalls.single.q, 'lisbon\tsalt');
  });

  _run('text_over_80_characters_is_not_sent', (async, rig) {
    rig.ctl.setInput('x' * 81);
    async.elapse(_debounce);
    expect(rig.api.searchCalls, isEmpty);
    expect(rig.state.tooLong, isTrue);
    rig.ctl.setInput('x' * 80);
    async.elapse(_debounce);
    expect(rig.api.searchCalls, hasLength(1));
  });

  _run('stale_response_for_old_query_is_ignored', (async, rig) {
    rig.ctl.setInput('ca');
    async.elapse(_debounce);
    rig.ctl.setInput('cam');
    async.elapse(_debounce);
    final old = rig.api.searchCalls[0];
    final fresh = rig.api.searchCalls[1];
    fresh.reply(artists: ['new']);
    async.flushMicrotasks();
    // The abandoned request answers last; the fake ignores cancellation.
    old.reply(artists: ['old'], next: 'old-cursor');
    async.flushMicrotasks();
    expect(rig.artistIds, ['new']);
    expect(rig.state.artists?.nextCursor, isNull);
  });

  _run('stale_error_for_old_query_is_ignored', (async, rig) {
    rig.ctl.setInput('ca');
    async.elapse(_debounce);
    rig.ctl.setInput('cam');
    async.elapse(_debounce);
    rig.api.searchCalls[1].reply(artists: ['new']);
    rig.api.searchCalls[0].fail();
    async.flushMicrotasks();
    expect(rig.artistIds, ['new']);
    expect(rig.state.artists?.error, isNull);
  });

  _run('tabs_switch_result_type', (async, rig) {
    rig.ctl.setInput('salt');
    async.elapse(_debounce);
    rig.api.searchCalls[0].reply(artists: ['a1']);
    async.flushMicrotasks();
    rig.ctl.selectTab(SearchTab.stories);
    async.flushMicrotasks();
    expect(rig.api.searchCalls, hasLength(2));
    expect(rig.api.searchCalls[1].type, 'stories');
    expect(rig.api.searchCalls[1].q, 'salt');
    rig.api.searchCalls[1].reply(stories: ['w1', 'w2']);
    async.flushMicrotasks();
    expect(rig.storyIds, ['w1', 'w2']);
    // Back to artists: already loaded, no new request.
    rig.ctl.selectTab(SearchTab.artists);
    async.flushMicrotasks();
    expect(rig.api.searchCalls, hasLength(2));
    expect(rig.artistIds, ['a1']);
  });

  _run('a_late_answer_for_the_other_tab_never_lands_in_the_visible_one', (
    async,
    rig,
  ) {
    rig.ctl.setInput('salt');
    async.elapse(_debounce);
    final artistsCall = rig.api.searchCalls[0];
    rig.ctl.selectTab(SearchTab.stories);
    async.flushMicrotasks();
    rig.api.searchCalls[1].reply(stories: ['w1']);
    async.flushMicrotasks();
    artistsCall.reply(artists: ['a-late']);
    async.flushMicrotasks();
    expect(rig.state.tab, SearchTab.stories);
    expect(rig.storyIds, ['w1']);
    expect(rig.state.stories?.items.length, 1);
  });

  _run('a_new_query_discards_the_other_tabs_old_results', (async, rig) {
    rig.ctl.setInput('salt');
    async.elapse(_debounce);
    rig.api.searchCalls[0].reply(artists: ['a1']);
    async.flushMicrotasks();
    rig.ctl.selectTab(SearchTab.stories);
    async.flushMicrotasks();
    rig.api.searchCalls[1].reply(stories: ['w1']);
    async.flushMicrotasks();
    rig.ctl.setInput('rope');
    async.elapse(_debounce);
    expect(rig.state.artists, isNull);
    expect(rig.state.stories?.isLoading, isTrue);
    expect(rig.api.searchCalls[2].type, 'stories');
    expect(rig.api.searchCalls[2].q, 'rope');
  });

  _run('session_flip_ignores_a_response_from_before_and_searches_again', (
    async,
    rig,
  ) {
    rig.ctl.setInput('salt');
    async.elapse(_debounce);
    final before = rig.api.searchCalls[0];
    rig.container.read(sessionEpochProvider.notifier).bump();
    async.flushMicrotasks();
    expect(rig.api.searchCalls, hasLength(2));
    rig.api.searchCalls[1].reply(artists: ['after']);
    async.flushMicrotasks();
    before.reply(artists: ['before']);
    async.flushMicrotasks();
    expect(rig.artistIds, ['after']);
    expect(rig.state.input, 'salt');
  });

  _run('pagination_sends_the_cursor_and_appends_without_duplicates', (
    async,
    rig,
  ) {
    rig.ctl.setInput('salt');
    async.elapse(_debounce);
    rig.api.searchCalls[0].reply(artists: ['a1', 'a2'], next: 'c1');
    async.flushMicrotasks();
    rig.ctl.loadMore();
    rig.ctl.loadMore(); // a second call while one is running does nothing
    async.flushMicrotasks();
    expect(rig.api.searchCalls, hasLength(2));
    expect(rig.api.searchCalls[1].cursor, 'c1');
    rig.api.searchCalls[1].reply(artists: ['a2', 'a3']);
    async.flushMicrotasks();
    expect(rig.artistIds, ['a1', 'a2', 'a3']);
    rig.ctl.loadMore(); // no cursor left
    async.flushMicrotasks();
    expect(rig.api.searchCalls, hasLength(2));
  });

  _run('a_page_for_an_old_query_is_dropped', (async, rig) {
    rig.ctl.setInput('salt');
    async.elapse(_debounce);
    rig.api.searchCalls[0].reply(artists: ['a1'], next: 'c1');
    async.flushMicrotasks();
    rig.ctl.loadMore();
    final page = rig.api.searchCalls[1];
    rig.ctl.setInput('rope');
    async.elapse(_debounce);
    rig.api.searchCalls[2].reply(artists: ['r1']);
    async.flushMicrotasks();
    page.reply(artists: ['stale-page'], next: 'x');
    async.flushMicrotasks();
    expect(rig.artistIds, ['r1']);
    expect(rig.state.artists?.nextCursor, isNull);
  });

  _run('a_later_page_failure_keeps_items_and_retry_reuses_the_cursor', (
    async,
    rig,
  ) {
    rig.ctl.setInput('salt');
    async.elapse(_debounce);
    rig.api.searchCalls[0].reply(artists: ['a1'], next: 'c1');
    async.flushMicrotasks();
    rig.ctl.loadMore();
    rig.api.searchCalls[1].failOffline();
    async.flushMicrotasks();
    expect(rig.artistIds, ['a1']);
    expect(rig.state.artists?.moreError, isNotNull);
    rig.ctl.retryMore();
    async.flushMicrotasks();
    expect(rig.api.searchCalls[2].cursor, 'c1');
    rig.api.searchCalls[2].reply(artists: ['a2']);
    async.flushMicrotasks();
    expect(rig.artistIds, ['a1', 'a2']);
  });

  _run('failed_search_keeps_the_query_and_retry_asks_again', (async, rig) {
    rig.ctl.setInput('salt');
    async.elapse(_debounce);
    rig.api.searchCalls[0].fail();
    async.flushMicrotasks();
    expect(rig.state.artists?.error, isNotNull);
    rig.ctl.retry();
    async.flushMicrotasks();
    expect(rig.api.searchCalls, hasLength(2));
    expect(rig.api.searchCalls[1].q, 'salt');
    rig.api.searchCalls[1].reply(artists: ['a1']);
    async.flushMicrotasks();
    expect(rig.artistIds, ['a1']);
    expect(rig.state.artists?.error, isNull);
  });

  _run('submit_searches_at_once_and_remembers_the_query', (async, rig) {
    rig.ctl.setInput('  Salt  ');
    rig.ctl.submit();
    async.flushMicrotasks();
    expect(rig.api.searchCalls.single.q, 'Salt');
    async.elapse(_debounce);
    expect(rig.api.searchCalls, hasLength(1)); // the debounce timer is gone
    expect(rig.container.read(recentSearchesProvider), ['Salt']);
  });

  _run('typing_alone_never_remembers_a_query', (async, rig) {
    rig.ctl.setInput('salt');
    async.elapse(_debounce);
    rig.api.searchCalls[0].reply(artists: ['a1']);
    async.flushMicrotasks();
    expect(rig.container.read(recentSearchesProvider), isEmpty);
  });

  _run('disposing_with_a_request_in_flight_is_safe', (async, rig) {
    rig.ctl.setInput('salt');
    async.elapse(_debounce);
    final call = rig.api.searchCalls[0];
    rig.container.dispose();
    call.reply(artists: ['a1']);
    async.flushMicrotasks();
  });

  _run('pending_debounce_timer_is_cancelled_on_dispose', (async, rig) {
    rig.ctl.setInput('salt');
    rig.container.dispose();
    async.elapse(_debounce * 2);
    expect(rig.api.searchCalls, isEmpty);
  });
}
