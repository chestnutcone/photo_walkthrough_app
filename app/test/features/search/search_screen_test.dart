import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'support/fake_search_api.dart';
import 'support/memory_recents.dart';
import 'support/search_harness.dart';

Finder _key(String k) => find.byKey(ValueKey(k));

void main() {
  testWidgets('empty_state_shows_suggested_artists', (tester) async {
    final h = await pumpSearch(tester);
    expect(find.text('SUGGESTED ARTISTS'), findsOneWidget);
    expect(find.text('Artist s1'), findsOneWidget);
    expect(find.text('Artist s2'), findsOneWidget);
    expect(h.api.searchCalls, isEmpty);
    expect(h.signInLaunches, isEmpty); // guests may browse
  });

  testWidgets('suggested_still_loading_does_not_block_the_search_box', (
    tester,
  ) async {
    final api = FakeSearchApi()..autoSuggested = false;
    await pumpSearch(tester, api: api);
    expect(tester.takeException(), isNull);
    expect(find.text('SUGGESTED ARTISTS'), findsNothing);
    expect(_key('search-field'), findsOneWidget);
  });

  testWidgets('one_character_shows_a_hint_and_sends_nothing', (tester) async {
    final h = await pumpSearch(tester);
    await typeQuery(tester, 'a');
    expect(find.text('Type at least 2 characters'), findsOneWidget);
    expect(h.api.searchCalls, isEmpty);
    expect(find.text('Artist s1'), findsOneWidget); // empty state stays
  });

  testWidgets('typing_searches_after_the_debounce_and_lists_artists', (
    tester,
  ) async {
    final h = await pumpSearch(tester);
    await tester.enterText(_key('search-field'), 'ine');
    await tester.pump(const Duration(milliseconds: 299));
    expect(h.api.searchCalls, isEmpty);
    await tester.pump(const Duration(milliseconds: 1));
    expect(h.api.searchCalls, hasLength(1));
    expect(find.byType(CircularProgressIndicator), findsOneWidget);
    h.api.searchCalls.single.reply(artists: ['a1', 'a2']);
    await tester.pump();
    await tester.pump();
    expect(_key('search-artist-a1'), findsOneWidget);
    expect(find.text('Artist a2'), findsOneWidget);
    expect(find.text('SUGGESTED ARTISTS'), findsNothing);
  });

  testWidgets('tabs_switch_result_type', (tester) async {
    final h = await pumpSearch(tester);
    await typeQuery(tester, 'salt');
    h.api.searchCalls[0].reply(artists: ['a1']);
    await tester.pump();
    await tester.pump();
    await tester.tap(_key('search-tab-stories'));
    await tester.pump();
    expect(h.api.searchCalls[1].type, 'stories');
    h.api.searchCalls[1].reply(stories: ['w1']);
    await tester.pump();
    await tester.pump();
    expect(_key('search-story-w1'), findsOneWidget);
    expect(find.text('Title w1'), findsOneWidget);
    expect(_key('search-artist-a1'), findsNothing);
    await tester.tap(_key('search-tab-artists'));
    await tester.pump();
    expect(_key('search-artist-a1'), findsOneWidget);
    expect(h.api.searchCalls, hasLength(2));
  });

  testWidgets('no_results_message', (tester) async {
    final h = await pumpSearch(tester);
    await typeQuery(tester, 'zzzz');
    h.api.searchCalls.single.reply();
    await tester.pump();
    await tester.pump();
    expect(find.text('No artists found'), findsOneWidget);
    await tester.tap(_key('search-tab-stories'));
    await tester.pump();
    h.api.searchCalls[1].reply();
    await tester.pump();
    await tester.pump();
    expect(find.text('No stories found'), findsOneWidget);
  });

  testWidgets('error_shows_retry_and_offline_wording', (tester) async {
    final h = await pumpSearch(tester);
    await typeQuery(tester, 'salt');
    h.api.searchCalls[0].failOffline();
    await tester.pump();
    await tester.pump();
    expect(find.text("You're offline"), findsOneWidget);
    await tester.tap(_key('search-retry'));
    await tester.pump();
    expect(h.api.searchCalls, hasLength(2));
    h.api.searchCalls[1].fail();
    await tester.pump();
    await tester.pump();
    expect(find.text("Couldn't search"), findsOneWidget);
    await tester.tap(_key('search-retry'));
    await tester.pump();
    h.api.searchCalls[2].reply(artists: ['a1']);
    await tester.pump();
    await tester.pump();
    expect(_key('search-artist-a1'), findsOneWidget);
  });

  testWidgets('rate_limited_search_says_to_wait', (tester) async {
    final h = await pumpSearch(tester);
    await typeQuery(tester, 'salt');
    h.api.searchCalls[0].fail(code: 'rate_limited', status: 429);
    await tester.pump();
    await tester.pump();
    expect(find.text('Too many searches'), findsOneWidget);
  });

  testWidgets('tapping_results_navigates_and_remembers_the_query', (
    tester,
  ) async {
    final h = await pumpSearch(tester);
    await typeQuery(tester, 'salt');
    h.api.searchCalls[0].reply(artists: ['a1']);
    await tester.pump();
    await tester.pump();
    expect(h.recents.saved, isEmpty);
    await tester.tap(_key('search-artist-a1'));
    await tester.pumpAndSettle();
    expect(find.text('route:artist/ha1'), findsOneWidget);
    expect(h.recents.saved, ['salt']);
  });

  testWidgets('tapping_a_story_opens_it', (tester) async {
    final h = await pumpSearch(tester);
    await typeQuery(tester, 'salt');
    h.api.searchCalls[0].reply();
    await tester.pump();
    await tester.tap(_key('search-tab-stories'));
    await tester.pump();
    h.api.searchCalls[1].reply(stories: ['w9']);
    await tester.pump();
    await tester.pump();
    await tester.tap(_key('search-story-w9'));
    await tester.pumpAndSettle();
    expect(find.text('route:story/w9'), findsOneWidget);
  });

  testWidgets('keyboard_search_action_searches_at_once_and_remembers', (
    tester,
  ) async {
    final h = await pumpSearch(tester);
    await tester.enterText(_key('search-field'), 'salt');
    await tester.testTextInput.receiveAction(TextInputAction.search);
    await tester.pump();
    expect(h.api.searchCalls, hasLength(1));
    expect(h.recents.saved, ['salt']);
  });

  testWidgets('recent_searches_show_run_again_and_clear', (tester) async {
    final recents = MemoryRecentsStore(['salt', 'rope']);
    final h = await pumpSearch(tester, recents: recents);
    expect(find.text('RECENT'), findsOneWidget);
    expect(find.text('salt'), findsOneWidget);
    await tester.tap(_key('recent-0'));
    await tester.pump();
    expect(h.api.searchCalls.single.q, 'salt');
    expect(
      tester.widget<EditableText>(find.byType(EditableText)).controller.text,
      'salt',
    );
    h.api.searchCalls.single.reply(artists: ['a1']);
    await tester.pump();
    await tester.pump();
    // Clearing the box returns to the empty state with the recents.
    await tester.tap(_key('search-clear'));
    await tester.pump();
    await tester.tap(_key('recent-clear'));
    await tester.pump();
    await tester.pump();
    expect(find.text('RECENT'), findsNothing);
    expect(recents.saved, isEmpty);
  });

  testWidgets('follow_buttons_share_state_with_the_follow_overrides', (
    tester,
  ) async {
    final h = await pumpSearch(tester, signedIn: true);
    await tester.pumpAndSettle();
    expect(find.text('Follow'), findsNWidgets(2));
    await tester.tap(_key('search-follow-s1'));
    await tester.pump();
    expect(h.api.followCalls.single.id, 's1');
    expect(h.api.followCalls.single.follow, isTrue);
    h.api.followCalls.single.succeed();
    await tester.pump();
    expect(
      find.descendant(
        of: _key('search-follow-s1'),
        matching: find.text('Following'),
      ),
      findsOneWidget,
    );
    // The same artist in the results reads the shared state, not the
    // server's stale `isFollowing: false`.
    await typeQuery(tester, 'salt');
    h.api.searchCalls.single.reply(artists: ['s1', 'other']);
    await tester.pump();
    await tester.pump();
    expect(
      find.descendant(
        of: _key('search-follow-s1'),
        matching: find.text('Following'),
      ),
      findsOneWidget,
    );
    expect(
      find.descendant(
        of: _key('search-follow-other'),
        matching: find.text('Follow'),
      ),
      findsOneWidget,
    );
  });

  testWidgets('guest_tapping_follow_is_asked_to_sign_in', (tester) async {
    final h = await pumpSearch(tester);
    await tester.tap(_key('search-follow-s1'));
    await tester.pump();
    expect(h.signInLaunches, hasLength(1));
    expect(h.api.followCalls, isEmpty);
  });

  testWidgets('reaching_the_end_loads_the_next_page_and_a_failure_can_retry', (
    tester,
  ) async {
    final h = await pumpSearch(tester);
    await typeQuery(tester, 'salt');
    h.api.searchCalls[0].reply(
      artists: [for (var i = 0; i < 12; i++) 'p$i'],
      next: 'c1',
    );
    await tester.pump();
    await tester.pump();
    await tester.drag(find.byType(Scrollable).last, const Offset(0, -2000));
    await tester.pump();
    expect(h.api.searchCalls, hasLength(2));
    expect(h.api.searchCalls[1].cursor, 'c1');
    h.api.searchCalls[1].failOffline();
    await tester.pump();
    await tester.pump();
    expect(_key('more-retry'), findsOneWidget);
    await tester.drag(find.byType(Scrollable).last, const Offset(0, -2000));
    await tester.pump();
    await tester.tap(_key('more-retry'));
    await tester.pump();
    expect(h.api.searchCalls, hasLength(3));
    h.api.searchCalls[2].reply(artists: ['p-last']);
    await tester.pump();
    await tester.pump();
    await tester.drag(find.byType(Scrollable).last, const Offset(0, -4000));
    await tester.pump();
    expect(find.text('Artist p-last'), findsOneWidget);
  });

  testWidgets('leaving_the_screen_with_a_request_in_flight_is_safe', (
    tester,
  ) async {
    final h = await pumpSearch(tester);
    await typeQuery(tester, 'salt');
    final call = h.api.searchCalls.single;
    h.router.go('/');
    await tester.pumpAndSettle();
    call.reply(artists: ['a1']);
    await tester.pump();
    expect(tester.takeException(), isNull);
    expect(find.text('route:feed'), findsOneWidget);
  });

  testWidgets('leaving_the_screen_cancels_the_pending_debounce', (
    tester,
  ) async {
    final h = await pumpSearch(tester);
    await tester.enterText(_key('search-field'), 'salt');
    h.router.go('/');
    await tester.pumpAndSettle();
    await tester.pump(const Duration(seconds: 1));
    expect(h.api.searchCalls, isEmpty);
  });

  testWidgets('tap_targets_are_at_least_44_points_and_labelled', (
    tester,
  ) async {
    final h = await pumpSearch(tester, signedIn: true);
    await typeQuery(tester, 'salt');
    h.api.searchCalls.single.reply(artists: ['a1']);
    await tester.pump();
    await tester.pump();
    for (final k in [
      'search-tab-artists',
      'search-tab-stories',
      'search-artist-a1',
      'search-follow-a1',
      'search-clear',
      'search-back',
    ]) {
      final size = tester.getSize(_key(k));
      expect(size.height, greaterThanOrEqualTo(44), reason: k);
      expect(size.width, greaterThanOrEqualTo(44), reason: k);
    }
    final handle = tester.ensureSemantics();
    expect(find.bySemanticsLabel('Artist Artist a1'), findsOneWidget);
    expect(find.bySemanticsLabel('Follow Artist a1'), findsOneWidget);
    expect(find.bySemanticsLabel('Search artists and stories'), findsOneWidget);
    handle.dispose();
  });

  testWidgets(
    'hostile_query_text_is_shown_and_sent_verbatim_but_never_logged',
    (tester) async {
      final logs = <String>[];
      final old = debugPrint;
      debugPrint = (m, {wrapWidth}) => logs.add(m ?? '');
      final h = await pumpSearch(tester);
      const q = '<b>secret-term</b> مرحبا 😀';
      await typeQuery(tester, q);
      h.api.searchCalls.single.fail();
      await tester.pump();
      await tester.pump();
      expect(h.api.searchCalls.single.q, q);
      debugPrint = old;
      expect(logs.where((l) => l.contains('secret-term')), isEmpty);
      expect(tester.takeException(), isNull);
    },
  );
}
