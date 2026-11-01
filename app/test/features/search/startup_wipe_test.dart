import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:vela_app/core/api/api_providers.dart';
import 'package:vela_app/core/auth/local_data.dart';
import 'package:vela_app/core/auth/session_store.dart';
import 'package:vela_app/main.dart';
import 'package:vela_app/features/search/recent_searches.dart';

import '../../core/support/fakes.dart';
import 'support/memory_recents.dart';

void main() {
  testWidgets(
    'delete_account_wipe_clears_recent_searches_even_if_search_was_never_opened',
    (tester) async {
      final store = MemoryRecentsStore(['salt', 'rope']);
      await tester.pumpWidget(
        ProviderScope(
          overrides: [
            sessionStorageProvider.overrideWithValue(FakeSessionStorage()),
            recentSearchesStoreProvider.overrideWithValue(store),
            httpClientAdapterProvider.overrideWithValue(
              FakeAdapter((_) => jsonResponse(404, errEnvelope('not_found'))),
            ),
          ],
          child: const VelaApp(),
        ),
      );
      await tester.pump();
      final container = ProviderScope.containerOf(
        tester.element(find.byType(MaterialApp)),
      );
      await container.read(wipeLocalDataProvider)();
      expect(store.saved, isEmpty);
      expect(store.clears, 1);
      await tester.pumpWidget(const SizedBox());
      await tester.pump(const Duration(seconds: 5));
    },
  );
}
