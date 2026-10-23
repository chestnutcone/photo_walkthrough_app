import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:vela_api_client/vela_api_client.dart';

import '../../core/api/api_exception.dart';
import '../../core/api/api_providers.dart';

/// One story. Failed loads are not retried automatically; the screen offers
/// an explicit retry.
final workProvider = FutureProvider.autoDispose.family<Work, String>(
  (ref, id) => apiGuard(() async {
    final res = await ref.watch(apiProvider).getWorksApi().getWork(id: id);
    final env = res.data;
    return unwrapEnvelope(
      success: env?.success ?? false,
      data: env?.data,
      error: env?.error,
    );
  }),
  retry: (_, _) => null,
);

/// Missing, removed, blocked and private stories all look the same to the
/// viewer, so existence is not leaked.
bool isStoryUnavailable(Object error) =>
    error is ApiException &&
    (const {403, 404, 410}.contains(error.statusCode) ||
        error.code == 'not_found');
