import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../api/api_providers.dart';

/// Cleanups for data kept on the device for the signed-in user (recent
/// searches, caches, flags). Features that add such storage register a
/// cleaner here, so account deletion cannot forget it. Today the only
/// on-device state is the session in secure storage and in-memory providers
/// that reset with the session epoch.
final localDataCleanersProvider = Provider<List<Future<void> Function()>>(
  (_) => const [],
);

/// Removes everything the app keeps for this account: runs every cleaner
/// (a failing one never stops the rest), then ends the session, which
/// deletes the stored tokens and bumps the epoch so per-user providers
/// (profile, feed, drafts, onboarding flags) rebuild empty.
final wipeLocalDataProvider = Provider<Future<void> Function()>((ref) {
  return () async {
    for (final clean in ref.read(localDataCleanersProvider)) {
      try {
        await clean();
      } on Object {
        // Best effort: carry on with the remaining cleaners.
      }
    }
    await ref.read(sessionProvider.notifier).clear();
  };
});
