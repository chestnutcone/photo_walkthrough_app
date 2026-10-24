import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:vela_api_client/vela_api_client.dart';

import '../../core/api/api_providers.dart';
import '../../core/auth/local_data.dart';

/// Revokes the refresh token on a best-effort basis, then always clears the
/// session; the router sends a guest back to the feed. Device data (see
/// `local_data.dart`) is kept, except cleaners registered `onSignOut`
/// (data a next account on a shared device must not see); only account
/// deletion wipes everything.
final signOutProvider = Provider<Future<void> Function()>((ref) {
  return () async {
    final notifier = ref.read(sessionProvider.notifier);
    final api = ref.read(apiProvider).getAuthApi();
    try {
      final session = await notifier.current();
      if (session != null) {
        await api
            .logout(
              logoutRequest: LogoutRequest(refreshToken: session.refreshToken),
            )
            .timeout(const Duration(seconds: 5));
      }
    } on Object {
      // Offline or already revoked: signing out locally still works.
    }
    await LocalDataRegistry.runAll(
      ref.read(localDataRegistryProvider).signOutCleaners,
    );
    await notifier.clear();
  };
});
