import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:vela_app/core/api/api_providers.dart';
import 'package:vela_app/core/auth/session_store.dart';
import 'package:vela_app/core/router.dart';
import 'package:vela_app/core/theme/app_theme.dart';
import 'package:vela_app/features/story/replies_sheet_hook.dart';
import 'package:vela_app/features/story/story_deeplink.dart';
import 'package:vela_app/features/story/story_settings.dart';

import '../../core/support/fakes.dart';
import 'support/story_harness.dart';

void main() {
  group('StoryDeepLink.fromQuery', () {
    test('reads_a_chapter_and_the_replies_flag', () {
      final l = StoryDeepLink.fromQuery({'chapter': '2', 'replies': '1'});
      expect(l.chapter, 2);
      expect(l.openReplies, isTrue);
    });

    test('garbage_negative_or_huge_chapters_are_ignored', () {
      for (final bad in ['x', '-1', '', '1.5', '100000', ' 2']) {
        final l = StoryDeepLink.fromQuery({'chapter': bad, 'replies': '1'});
        expect(l.chapter, isNull, reason: bad);
        expect(l.openReplies, isFalse, reason: bad);
      }
    });

    test('replies_needs_a_chapter_and_exactly_1', () {
      expect(StoryDeepLink.fromQuery({'replies': '1'}).openReplies, isFalse);
      expect(
        StoryDeepLink.fromQuery({'chapter': '1', 'replies': 'true'})
            .openReplies,
        isFalse,
      );
      expect(StoryDeepLink.fromQuery({'chapter': '1'}).openReplies, isFalse);
    });

    test('location_round_trips', () {
      final loc = storyLocation('w1', chapter: 3, openReplies: true);
      expect(loc, '/story/w1?chapter=3&replies=1');
      expect(storyLocation('w1'), '/story/w1');
      expect(Uri.parse(loc).queryParameters['chapter'], '3');
    });
  });

  group('StoryScreen deep link', () {
    final calls = <String>[];
    setUp(calls.clear);
    final sheet = openRepliesSheetProvider.overrideWithValue((
      ctx,
      workId,
      i, {
      required readOnly,
    }) async {
      calls.add('$workId/$i');
    });

    testWidgets('opens_the_chapter_and_its_replies', (t) async {
      await pumpStory(
        t,
        initialChapter: 1,
        openReplies: true,
        overrides: [sheet],
      );
      await t.pumpAndSettle();
      expect(kicker(2, 3), findsOneWidget);
      expect(calls, ['w1/1']);
    });

    testWidgets('opens_just_the_chapter_without_the_flag', (t) async {
      await pumpStory(t, initialChapter: 2, overrides: [sheet]);
      expect(kicker(3, 3), findsOneWidget);
      expect(calls, isEmpty);
    });

    testWidgets('a_chapter_past_the_end_is_ignored', (t) async {
      await pumpStory(
        t,
        initialChapter: 7,
        openReplies: true,
        overrides: [sheet],
      );
      expect(kicker(1, 3), findsNothing);
      expect(find.text('Salt and Rope'), findsWidgets);
      expect(calls, isEmpty);
    });

    testWidgets('no_deep_link_starts_at_the_intro', (t) async {
      await pumpStory(t, overrides: [sheet]);
      expect(kicker(1, 3), findsNothing);
      expect(calls, isEmpty);
    });
  });

  testWidgets('the_real_router_passes_the_query_to_the_story', (t) async {
    final calls = <String>[];
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
        openRepliesSheetProvider.overrideWithValue((
          ctx,
          workId,
          i, {
          required readOnly,
        }) async {
          calls.add('$workId/$i');
        }),
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
    router.go('/story/w1?chapter=1&replies=1');
    await t.pumpAndSettle();
    expect(kicker(2, 3), findsOneWidget);
    expect(calls, ['w1/1']);
  });
}
