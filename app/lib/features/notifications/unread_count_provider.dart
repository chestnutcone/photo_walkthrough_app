import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_exception.dart';
import '../../core/api/api_providers.dart';

/// Number of unread notifications for the signed-in viewer; 0 for guests.
/// Failures also read as 0: a badge must never break the screen it sits on.
/// Refetched (`ref.invalidate`) by the feed on pull-to-refresh and by the
/// notifications screen on resume, pull-to-refresh and every mark-read.
final unreadCountProvider = FutureProvider<int>((ref) async {
  // Keyed on sign-in/out, not on the session object: a token refresh must
  // not refetch the count.
  ref.watch(sessionEpochProvider);
  final session = await ref.read(sessionProvider.future);
  if (session == null) return 0;
  try {
    final env = (await apiGuard(
      () => ref.read(apiProvider).getNotificationsApi().unreadCount(),
    )).data;
    return unwrapEnvelope(
      success: env?.success ?? false,
      data: env?.data,
      error: env?.error,
    ).count;
  } on ApiException {
    return 0;
  }
});
