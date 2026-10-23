import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../api/api_providers.dart';

typedef LocalDataCleaner = Future<void> Function();

/// Where features register how to erase what they keep on the device for the
/// signed-in user (recent searches, caches, drafts on disk, flags).
///
/// Features add themselves with [registerLocalDataCleaner] from their own
/// code; this file never needs editing. Registering touches no provider
/// state, so it is safe inside a provider's `build`. A name registered twice
/// replaces the earlier cleaner (rebuilds do not stack).
///
/// Persisted stores must be cleanable even if the feature's provider was
/// never built this run, so a cleaner should work on the store itself and
/// the registering provider should be watched at startup.
class LocalDataRegistry {
  final Map<String, LocalDataCleaner> _cleaners = {};

  void register(String name, LocalDataCleaner cleaner) =>
      _cleaners[name] = cleaner;

  List<LocalDataCleaner> get cleaners => List.unmodifiable(_cleaners.values);
}

final localDataRegistryProvider = Provider<LocalDataRegistry>(
  (_) => LocalDataRegistry(),
);

/// Registers [cleaner] under [name] (for example `'recent-searches'`).
void registerLocalDataCleaner(Ref ref, String name, LocalDataCleaner cleaner) =>
    ref.read(localDataRegistryProvider).register(name, cleaner);

/// Account deletion only: runs every registered cleaner (a failing one never
/// stops the rest), then ends the session, which deletes the stored tokens
/// and bumps the epoch so per-user providers rebuild empty.
///
/// Policy: plain sign-out (`signOutProvider`) clears the session only
/// and leaves device data alone; deleting the account erases everything.
final wipeLocalDataProvider = Provider<Future<void> Function()>((ref) {
  return () async {
    for (final clean in ref.read(localDataRegistryProvider).cleaners) {
      try {
        await clean();
      } on Object {
        // Best effort: carry on with the remaining cleaners.
      }
    }
    await ref.read(sessionProvider.notifier).clear();
  };
});
