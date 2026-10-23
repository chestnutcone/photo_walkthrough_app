import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_exception.dart';
import '../../core/api/api_providers.dart';
import 'feed_controller.dart';

/// Follow state the viewer has set this session, keyed by author id. Every
/// card by the same author reads from here, so they flip together.
final followOverridesProvider =
    NotifierProvider<FollowOverrides, Map<String, bool>>(FollowOverrides.new);

class FollowOverrides extends Notifier<Map<String, bool>> {
  final Set<String> _pending = {};

  @override
  Map<String, bool> build() {
    ref.watch(feedViewerSignedInProvider);
    return const {};
  }

  /// Flips follow for [authorId] optimistically. Returns false when the
  /// server call failed and the change was rolled back. A tap while the same
  /// author's previous request is still running is ignored (returns true).
  Future<bool> toggle(
    String authorId, {
    required bool currentlyFollowing,
  }) async {
    if (!_pending.add(authorId)) return true;
    state = {...state, authorId: !currentlyFollowing};
    try {
      final api = ref.read(apiProvider).getArtistsApi();
      final env = (await apiGuard(
        () => currentlyFollowing
            ? api.unfollow(id: authorId)
            : api.follow(id: authorId),
      )).data;
      unwrapEnvelope<bool>(
        success: env?.success ?? false,
        data: true,
        error: env?.error,
      );
      return true;
    } on ApiException {
      if (ref.mounted) state = {...state, authorId: currentlyFollowing};
      return false;
    } finally {
      _pending.remove(authorId);
    }
  }
}
