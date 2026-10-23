import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:vela_api_client/vela_api_client.dart';

import '../../core/api/api_providers.dart';

/// Revokes the refresh token on a best-effort basis, then always clears the
/// session; the router sends a guest back to the feed.
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
    await notifier.clear();
  };
});
