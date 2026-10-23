import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:shared_preferences/shared_preferences.dart';
import 'package:vela_app/core/api/api_providers.dart';
import 'package:vela_app/core/auth/local_data.dart';
import 'package:vela_app/core/auth/session_store.dart';
import 'package:vela_app/features/search/recent_searches.dart';

import '../../core/support/fakes.dart';
import 'support/memory_recents.dart';

ProviderContainer _make(MemoryRecentsStore store) {
  final c = ProviderContainer(
    overrides: [
      recentSearchesStoreProvider.overrideWithValue(store),
      sessionStorageProvider.overrideWithValue(FakeSessionStorage()),
    ],
  );
  addTearDown(c.dispose);
  c.listen(recentSearchesProvider, (_, _) {});
  return c;
}

Future<void> _settle() => Future<void>.delayed(Duration.zero);

void main() {
  group('pure rules', () {
    test('addRecent_trims_dedupes_case_insensitively_and_moves_to_top', () {
      var list = addRecent(const [], '  Lisbon ');
      list = addRecent(list, 'salt');
      list = addRecent(list, 'LISBON');
      expect(list, ['LISBON', 'salt']);
    });

    test('addRecent_never_stores_queries_under_two_characters', () {
      expect(addRecent(const ['salt'], ' a '), ['salt']);
      expect(addRecent(const ['salt'], ''), ['salt']);
      expect(addRecent(const [], 'ab'), ['ab']);
    });

    test('addRecent_never_stores_text_over_the_search_limit', () {
      expect(addRecent(const [], 'x' * 81), isEmpty);
    });
  });

  test('recent_searches_dedupe_and_cap_at_10', () async {
    final store = MemoryRecentsStore();
    final c = _make(store);
    final ctl = c.read(recentSearchesProvider.notifier);
    for (var i = 1; i <= 12; i++) {
      await ctl.add('query $i');
    }
    var list = c.read(recentSearchesProvider);
    expect(list, hasLength(10));
    expect(list.first, 'query 12');
    expect(list.last, 'query 3');
    await ctl.add('QUERY 5');
    list = c.read(recentSearchesProvider);
    expect(list, hasLength(10));
    expect(list.first, 'QUERY 5');
    expect(list.where((q) => q.toLowerCase() == 'query 5'), hasLength(1));
    expect(store.saved, list);
  });

  test('a_short_query_is_not_stored_or_written', () async {
    final store = MemoryRecentsStore(['salt']);
    final c = _make(store);
    await _settle();
    await c.read(recentSearchesProvider.notifier).add('x');
    expect(c.read(recentSearchesProvider), ['salt']);
  });

  test('saved_searches_load_at_start_and_an_early_add_is_not_lost', () async {
    final store = MemoryRecentsStore(['older']);
    final c = _make(store);
    // Added before the stored list finished loading.
    await c.read(recentSearchesProvider.notifier).add('newer');
    await _settle();
    expect(c.read(recentSearchesProvider), ['newer', 'older']);
    expect(store.saved, ['newer', 'older']);
  });

  test('an_unreadable_store_reads_as_empty', () async {
    final store = MemoryRecentsStore()..failLoad = true;
    final c = _make(store);
    await _settle();
    expect(c.read(recentSearchesProvider), isEmpty);
    await c.read(recentSearchesProvider.notifier).add('salt');
    expect(c.read(recentSearchesProvider), ['salt']);
  });

  test('clear_empties_state_and_store', () async {
    final store = MemoryRecentsStore(['a1', 'b2']);
    final c = _make(store);
    await _settle();
    await c.read(recentSearchesProvider.notifier).clear();
    expect(c.read(recentSearchesProvider), isEmpty);
    expect(store.saved, isEmpty);
  });

  group('delete-account wipe', () {
    test('wipe_clears_recent_searches_state_and_store', () async {
      final store = MemoryRecentsStore(['salt', 'rope']);
      final c = _make(store);
      await _settle();
      expect(c.read(recentSearchesProvider), ['salt', 'rope']);
      await c.read(wipeLocalDataProvider)();
      expect(store.saved, isEmpty);
      expect(c.read(recentSearchesProvider), isEmpty);
    });

    test('sign_out_leaves_recent_searches_alone', () async {
      // Policy: recent searches are device data, cleared on account deletion
      // only (documented in recent_searches.dart).
      final store = MemoryRecentsStore(['salt']);
      final c = _make(store);
      await _settle();
      c.read(sessionEpochProvider.notifier).bump();
      await _settle();
      expect(c.read(recentSearchesProvider), ['salt']);
      expect(store.clears, 0);
    });

    test('the_real_store_wipes_shared_preferences', () async {
      SharedPreferences.setMockInitialValues({
        'vela.recent_searches': ['salt', 'rope'],
        'unrelated': 'keep',
      });
      final c = ProviderContainer(
        overrides: [
          sessionStorageProvider.overrideWithValue(FakeSessionStorage()),
        ],
      );
      addTearDown(c.dispose);
      c.listen(recentSearchesProvider, (_, _) {});
      await _settle();
      await _settle();
      expect(c.read(recentSearchesProvider), ['salt', 'rope']);
      await c.read(wipeLocalDataProvider)();
      final prefs = await SharedPreferences.getInstance();
      expect(prefs.getStringList('vela.recent_searches'), isNull);
      expect(prefs.getString('unrelated'), 'keep');
      expect(c.read(recentSearchesProvider), isEmpty);
    });

    test('the_real_store_round_trips_a_search', () async {
      SharedPreferences.setMockInitialValues({});
      final c = ProviderContainer(
        overrides: [
          sessionStorageProvider.overrideWithValue(FakeSessionStorage()),
        ],
      );
      addTearDown(c.dispose);
      c.listen(recentSearchesProvider, (_, _) {});
      await c.read(recentSearchesProvider.notifier).add('Salt');
      final prefs = await SharedPreferences.getInstance();
      expect(prefs.getStringList('vela.recent_searches'), ['Salt']);
    });
  });
}
