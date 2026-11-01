import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:vela_app/core/api/api_providers.dart';
import 'package:vela_app/core/auth/session_store.dart';
import 'package:vela_app/core/router.dart';
import 'package:vela_app/core/theme/app_theme.dart';
import 'package:vela_app/features/story/story_deeplink.dart';
import 'package:vela_app/features/story/story_settings.dart';

import '../../core/support/fakes.dart';
import '../story/support/story_harness.dart';

void main() {
  testWidgets('deep_link_s_workid_routes_to_story', (t) async {
    final c = ProviderContainer(
      overrides: [
        sessionStorageProvider.overrideWithValue(FakeSessionStorage()),
        needsOnboardingProvider.overrideWithValue(false),
        httpClientAdapterProvider.overrideWithValue(
          FakeAdapter(
            (o) => o.path == '/v1/works/w1'
                ? jsonResponse(200, okEnvelope(workJson()))
                : jsonResponse(404, errEnvelope('not_found')),
          ),
        ),
        storyImageProviderFactoryProvider.overrideWithValue(
          (_) => MemoryImage(transparentPng),
        ),
      ],
    );
    addTearDown(c.dispose);
    await c.read(sessionProvider.future);
    final router = c.read(routerProvider);
    await t.pumpWidget(
      UncontrolledProviderScope(
        container: c,
        child: MaterialApp.router(theme: buildTheme(), routerConfig: router),
      ),
    );
    router.go('/s/w1');
    await t.pumpAndSettle();
    expect(router.routeInformationProvider.value.uri.path, '/story/w1');
    expect(find.text('Salt and Rope'), findsWidgets);
  });

  test('share_path_maps_to_story_and_keeps_the_query', () {
    expect(shareLinkRedirect(Uri.parse('/s/w1')), '/story/w1');
    expect(
      shareLinkRedirect(Uri.parse('/s/w1?chapter=2&replies=1')),
      '/story/w1?chapter=2&replies=1',
    );
    expect(shareLinkRedirect(Uri.parse('/s/a%20b')), '/story/a%20b');
  });
}
