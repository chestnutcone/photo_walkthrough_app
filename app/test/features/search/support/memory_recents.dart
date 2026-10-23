import 'package:vela_app/features/search/recent_searches.dart';

/// In-memory [RecentSearchesStore] for tests.
class MemoryRecentsStore implements RecentSearchesStore {
  MemoryRecentsStore([List<String> initial = const []]) : saved = [...initial];

  List<String> saved;
  int clears = 0;
  bool failLoad = false;

  @override
  Future<List<String>> load() async {
    if (failLoad) throw StateError('disk');
    return [...saved];
  }

  @override
  Future<void> save(List<String> queries) async => saved = [...queries];

  @override
  Future<void> clear() async {
    clears++;
    saved = [];
  }
}
