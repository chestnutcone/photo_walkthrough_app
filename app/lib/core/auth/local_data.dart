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
  final Set<String> _onSignOut = {};

  /// [onSignOut] marks data that must not outlive the signed-in account on a
  /// shared device (for example recent searches): it is also erased by plain
  /// sign-out. Other cleaners run on account deletion only.
  void register(
    String name,
    LocalDataCleaner cleaner, {
    bool onSignOut = false,
  }) {
    _cleaners[name] = cleaner;
    if (onSignOut) {
      _onSignOut.add(name);
    } else {
      _onSignOut.remove(name);
    }
  }

  /// Every cleaner: account deletion.
  List<LocalDataCleaner> get cleaners => List.unmodifiable(_cleaners.values);

  /// Only the session-scoped cleaners: plain sign-out.
  List<LocalDataCleaner> get signOutCleaners => List.unmodifiable([
    for (final e in _cleaners.entries)
      if (_onSignOut.contains(e.key)) e.value,
  ]);

  /// Runs [list] in order; a failing cleaner never stops the rest.
  static Future<void> runAll(List<LocalDataCleaner> list) async {
    for (final clean in list) {
      try {
        await clean();
      } on Object {
        // Best effort: carry on with the remaining cleaners.
      }
    }
  }
}

final localDataRegistryProvider = Provider<LocalDataRegistry>(
  (_) => LocalDataRegistry(),
);

/// Registers [cleaner] under [name] (for example `'recent-searches'`).
///
/// Pass [onSignOut] for data that a second account on the same device must
/// not see; plain sign-out then runs it too (account deletion runs all).
void registerLocalDataCleaner(
  Ref ref,
  String name,
  LocalDataCleaner cleaner, {
  bool onSignOut = false,
}) => ref
    .read(localDataRegistryProvider)
    .register(name, cleaner, onSignOut: onSignOut);

/// Account deletion only: runs every registered cleaner (a failing one never
/// stops the rest), then ends the session, which deletes the stored tokens
/// and bumps the epoch so per-user providers rebuild empty.
///
/// Policy: plain sign-out (`signOutProvider`) clears the session and only the
/// cleaners registered with `onSignOut`; deleting the account erases
/// everything.
final wipeLocalDataProvider = Provider<Future<void> Function()>((ref) {
  return () async {
    await LocalDataRegistry.runAll(
      ref.read(localDataRegistryProvider).cleaners,
    );
    await ref.read(sessionProvider.notifier).clear();
  };
});
