import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:vela_app/core/api/api_providers.dart';
import 'package:vela_app/core/auth/session_store.dart';
import 'package:vela_app/features/feed/feed_screen.dart';
import 'package:vela_app/main.dart';

import 'core/support/fakes.dart';

void main() {
  testWidgets('VelaApp builds a router MaterialApp showing the feed', (
    tester,
  ) async {
    await tester.pumpWidget(
      ProviderScope(
        overrides: [
          sessionStorageProvider.overrideWithValue(FakeSessionStorage()),
          httpClientAdapterProvider.overrideWithValue(
            FakeAdapter((_) => jsonResponse(404, errEnvelope('not_found'))),
          ),
        ],
        child: const VelaApp(),
      ),
    );
    await tester.pumpAndSettle();
    expect(find.byType(MaterialApp), findsOneWidget);
    expect(find.byType(FeedScreen), findsOneWidget);
  });
}
