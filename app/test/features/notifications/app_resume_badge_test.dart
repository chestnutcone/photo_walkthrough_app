import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:vela_app/core/api/api_providers.dart';
import 'package:vela_app/core/auth/session_store.dart';
import 'package:vela_app/main.dart';

import '../../core/support/fakes.dart';

void main() {
  testWidgets('resuming_the_app_refetches_the_unread_count_once', (
    tester,
  ) async {
    final adapter = FakeAdapter((o) {
      if (o.path == '/v1/notifications/unread-count') {
        return jsonResponse(200, okEnvelope({'count': 3}));
      }
      return jsonResponse(200, {
        'success': true,
        'data': <Object?>[],
        'error': null,
        'page': {'nextCursor': null},
      });
    });
    await tester.pumpWidget(
      ProviderScope(
        overrides: [
          sessionStorageProvider.overrideWithValue(
            FakeSessionStorage(
              Session(
                accessToken: 'A',
                refreshToken: 'R',
                accessExpiresAt: DateTime.utc(2030),
              ).encode(),
            ),
          ),
          httpClientAdapterProvider.overrideWithValue(adapter),
        ],
        child: const VelaApp(),
      ),
    );
    await tester.pumpAndSettle();
    final before = adapter.count('/v1/notifications/unread-count');
    expect(before, 1);

    for (final st in [
      AppLifecycleState.inactive,
      AppLifecycleState.hidden,
      AppLifecycleState.paused,
    ]) {
      tester.binding.handleAppLifecycleStateChanged(st);
    }
    await tester.pumpAndSettle();
    expect(adapter.count('/v1/notifications/unread-count'), before);

    for (final st in [
      AppLifecycleState.hidden,
      AppLifecycleState.inactive,
      AppLifecycleState.resumed,
    ]) {
      tester.binding.handleAppLifecycleStateChanged(st);
    }
    await tester.pumpAndSettle();
    expect(adapter.count('/v1/notifications/unread-count'), before + 1);

    await tester.pumpWidget(const SizedBox());
    await tester.pump(const Duration(seconds: 5));
  });
}
