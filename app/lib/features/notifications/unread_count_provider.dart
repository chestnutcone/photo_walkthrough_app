import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_exception.dart';
import '../../core/api/api_providers.dart';

/// Number of unread notifications for the signed-in viewer; 0 for guests.
/// Failures also read as 0: a badge must never break the screen it sits on.
/// B12 extends this (refresh after marking read).
final unreadCountProvider = FutureProvider<int>((ref) async {
  final session = await ref.watch(sessionProvider.future);
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
