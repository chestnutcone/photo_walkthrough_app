import 'dart:async';

import 'package:flutter/foundation.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:shared_preferences/shared_preferences.dart';

import '../../core/auth/local_data.dart';
import 'search_rules.dart';

/// Most recent searches kept.
const int maxRecentSearches = 10;

const String _localDataName = 'recent-searches';
const String _prefsKey = 'vela.recent_searches';

/// [list] with [query] on top: trimmed, deduplicated ignoring case (the old
/// entry moves to the top with the new spelling), capped at
/// [maxRecentSearches]. Text outside the searchable range is never stored.
@visibleForTesting
List<String> addRecent(List<String> list, String query) {
  final q = cleanQuery(query);
  if (!isSearchableQuery(q)) return list;
  final key = q.toLowerCase();
  return [
    q,
    for (final old in list)
      if (old.toLowerCase() != key) old,
  ].take(maxRecentSearches).toList();
}

/// Where recent searches live on the device. Injectable for tests.
abstract interface class RecentSearchesStore {
  Future<List<String>> load();
  Future<void> save(List<String> queries);
  Future<void> clear();
}

class PrefsRecentSearchesStore implements RecentSearchesStore {
  const PrefsRecentSearchesStore();

  @override
  Future<List<String>> load() async =>
      (await SharedPreferences.getInstance()).getStringList(_prefsKey) ??
      const [];

  @override
  Future<void> save(List<String> queries) async {
    await (await SharedPreferences.getInstance()).setStringList(
      _prefsKey,
      queries,
    );
  }

  @override
  Future<void> clear() async {
    await (await SharedPreferences.getInstance()).remove(_prefsKey);
  }
}

final recentSearchesStoreProvider = Provider<RecentSearchesStore>(
  (_) => const PrefsRecentSearchesStore(),
);

/// Recent searches, newest first.
///
/// Privacy: search text can be personal data. It stays on this device, is
/// never logged or sent to telemetry or crash reports, and nothing here
/// prints it.
///
/// Policy (ruling): a second account on a shared device must not see the
/// previous one's queries, so both plain sign-out and account deletion erase
/// them, through the local-data registry (registered `onSignOut`). The
/// registry holds the cleaner only once this provider has been built, so the
/// app root keeps it alive from startup.
final recentSearchesProvider = NotifierProvider<RecentSearches, List<String>>(
  RecentSearches.new,
);

class RecentSearches extends Notifier<List<String>> {
  Future<void> _loading = Future.value();

  @override
  List<String> build() {
    final store = ref.read(recentSearchesStoreProvider);
    registerLocalDataCleaner(
      ref,
      _localDataName,
      () => _wipe(store),
      onSignOut: true,
    );
    _loading = _load(store);
    return const [];
  }

  Future<void> _load(RecentSearchesStore store) async {
    try {
      final saved = await store.load();
      if (!ref.mounted) return;
      state = saved.take(maxRecentSearches).toList();
    } on Object {
      // An unreadable store is an empty history. The error is not logged:
      // it could carry the stored text.
    }
  }

  /// Remembers [query] (see [addRecent] for the rules).
  Future<void> add(String query) async {
    await _loading;
    if (!ref.mounted) return;
    final next = addRecent(state, query);
    if (identical(next, state)) return;
    state = next;
    try {
      await ref.read(recentSearchesStoreProvider).save(next);
    } on Object {
      // Best effort: the list still works for this run.
    }
  }

  Future<void> clear() => _wipe(ref.read(recentSearchesStoreProvider));

  Future<void> _wipe(RecentSearchesStore store) async {
    await _loading;
    await store.clear();
    if (ref.mounted) state = const [];
  }
}
