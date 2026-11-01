import 'package:flutter/widgets.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:vela_api_client/vela_api_client.dart';

import '../../core/api/api_exception.dart';
import '../../core/api/api_providers.dart';
import '../../core/require_sign_in.dart';

/// Follows or unfollows [artistId]. Returns the new follow state, or null when
/// the viewer declined to sign in. Throws [ApiException] when the call fails.
Future<bool?> toggleArtistFollow({
  required BuildContext context,
  required WidgetRef ref,
  required String artistId,
  required bool currentlyFollowing,
}) async {
  if (!await requireSignIn(context, ref)) return null;
  final api = ref.read(apiProvider).getArtistsApi();
  final Envelope? env = currentlyFollowing
      ? (await apiGuard(() => api.unfollow(id: artistId))).data
      : (await apiGuard(() => api.follow(id: artistId))).data;
  unwrapEnvelope<bool>(
    success: env?.success ?? false,
    data: true,
    error: env?.error,
  );
  return !currentlyFollowing;
}
