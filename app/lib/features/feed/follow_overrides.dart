import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_exception.dart';
import '../../core/api/api_providers.dart';
import 'feed_controller.dart';

/// What a [FollowOverrides.toggle] call did.
enum FollowResult {
  /// The server accepted the change.
  done,

  /// The server refused or was unreachable; the change was undone.
  rolledBack,

  /// Another request for the same author was still running; nothing happened.
  ignored,
}

/// Follow state the viewer has set this session, keyed by author id. Every
/// card by the same author reads from here, so they flip together. Resets
/// when the session identity changes.
final followOverridesProvider =
    NotifierProvider<FollowOverrides, Map<String, bool>>(FollowOverrides.new);

class FollowOverrides extends Notifier<Map<String, bool>> {
  Set<String> _pending = {};

  @override
  Map<String, bool> build() {
    ref.watch(feedViewerEpochProvider);
    _pending = {}; // requests of the previous session keep their old set
    return const {};
  }

  /// Flips follow for [authorId] optimistically and rolls back on failure.
  /// A call for an author with a request in flight is [FollowResult.ignored].
  Future<FollowResult> toggle(
    String authorId, {
    required bool currentlyFollowing,
  }) async {
    final pending = _pending;
    if (!pending.add(authorId)) return FollowResult.ignored;
    final epoch = ref.read(feedViewerEpochProvider);
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
      return FollowResult.done;
    } on ApiException {
      // Only undo into the session that made the change.
      if (ref.mounted && epoch == ref.read(feedViewerEpochProvider)) {
        state = {...state, authorId: currentlyFollowing};
      }
      return FollowResult.rolledBack;
    } finally {
      pending.remove(authorId);
    }
  }
}
