import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/require_sign_in.dart';
import '../feed/follow_overrides.dart';

/// Follow state for [id]: what the viewer set this session, else [fromServer].
bool effectiveFollowing(
  Map<String, bool> overrides,
  String id,
  bool? fromServer,
) => overrides[id] ?? fromServer ?? false;

/// Flips follow for [id] through the state shared with the feed and story
/// cards. Guests are asked to sign in first; a refused change is undone and
/// the viewer is told.
Future<void> toggleFollowFromProfile(
  BuildContext context,
  WidgetRef ref, {
  required String id,
  required bool currentlyFollowing,
}) async {
  if (!await requireSignIn(context, ref) || !context.mounted) return;
  final result = await ref
      .read(followOverridesProvider.notifier)
      .toggle(id, currentlyFollowing: currentlyFollowing);
  if (result == FollowResult.rolledBack && context.mounted) {
    ScaffoldMessenger.of(context).showSnackBar(
      const SnackBar(content: Text("Couldn't update follow. Try again.")),
    );
  }
}

/// The follower count to show: the server's, plus or minus the viewer's own
/// follow made this session.
int adjustedFollowerCount(int count, bool? serverFollowing, bool? override) {
  if (override == null) return count;
  final was = serverFollowing ?? false;
  if (override == was) return count;
  return override ? count + 1 : (count > 0 ? count - 1 : 0);
}
