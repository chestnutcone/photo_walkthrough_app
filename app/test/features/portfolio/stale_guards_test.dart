import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:vela_app/core/api/api_providers.dart';
import 'package:vela_app/features/portfolio/portfolio_data.dart';
import 'package:vela_app/features/portfolio/portfolio_screen.dart';

import 'support/fake_portfolio_api.dart';
import 'support/portfolio_harness.dart';

Finder key(String k) => find.byKey(ValueKey(k));

/// These tests drive the app through a fake client that ignores cancel
/// tokens, so each one fails if the matching stale-response guard is removed.
void main() {
  controllerTests();
  Future<void> frames(WidgetTester t) async {
    await t.pump(const Duration(milliseconds: 100));
    await t.pump(const Duration(milliseconds: 100));
  }

  Future<(FakePortfolioApi, PortfolioHarness)> pump(
    WidgetTester t, {
    String handle = 'ines',
    bool signedIn = false,
  }) async {
    final api = FakePortfolioApi();
    final h = await pumpPortfolio(
      t,
      handle: handle,
      me: signedIn ? meJson() : null,
      settle: false,
      overrides: [apiProvider.overrideWithValue(api)],
    );
    return (api, h);
  }

  void bumpSession(WidgetTester t) =>
      ProviderScope.containerOf(t.element(find.byType(PortfolioScreen)))
          .read(sessionEpochProvider.notifier)
          .bump();

  testWidgets('works_response_from_before_a_session_change_is_dropped', (
    t,
  ) async {
    final (api, _) = await pump(t);
    api.artist.single.done.complete(FakePortfolioApi.profile());
    await frames(t);
    expect(api.works, hasLength(1));
    bumpSession(t);
    await frames(t);
    // Artist reloads too; answer whatever is open for the new session.
    api.artist.last.done.complete(FakePortfolioApi.profile());
    await frames(t);
    expect(api.works.length, greaterThan(1));
    for (final call in api.works.skip(1)) {
      call.done.complete(FakePortfolioApi.workList([workJson('fresh')]));
    }
    await frames(t);
    api.works[0].done.complete(FakePortfolioApi.workList([workJson('stale')]));
    await frames(t);
    expect(find.text('Title stale'), findsNothing);
    expect(find.text('Title fresh'), findsWidgets);
  });

  testWidgets('followers_response_from_before_a_session_change_is_dropped', (
    t,
  ) async {
    final (api, _) = await pump(t, signedIn: true);
    api.artist.single.done.complete(FakePortfolioApi.profile(handle: 'ines'));
    await frames(t);
    api.works.single.done.complete(FakePortfolioApi.workList([]));
    await frames(t);
    // The profile's "followed by" line and the sheet share one list, so a
    // single request is open when the sheet shows.
    await t.tap(key('stat-followers'));
    await frames(t);
    final before = api.followers.length;
    expect(before, 1);
    final held = api.followers.first;
    bumpSession(t);
    await frames(t);
    api.artist.last.done.complete(FakePortfolioApi.profile());
    await frames(t);
    expect(api.followers.length, greaterThan(before));
    for (final call in api.followers.skip(1)) {
      call.done.complete(
        FakePortfolioApi.followerList([
          profileJson(id: 'f2', handle: 'new', name: 'Fresh'),
        ]),
      );
    }
    await frames(t);
    held.done.complete(
      FakePortfolioApi.followerList([
        profileJson(id: 'f1', handle: 'old', name: 'Stale'),
      ]),
    );
    await frames(t);
    expect(find.text('Stale'), findsNothing);
    expect(key('follower-row-f2'), findsOneWidget);
  });

  testWidgets('artist_response_from_before_a_session_change_is_dropped', (
    t,
  ) async {
    final (api, _) = await pump(t);
    final stale = api.artist.single;
    bumpSession(t);
    await frames(t);
    expect(api.artist, hasLength(2));
    api.artist[1].done.complete(FakePortfolioApi.profile(name: 'Fresh Name'));
    await frames(t);
    stale.done.complete(FakePortfolioApi.profile(name: 'Stale Name'));
    await frames(t);
    expect(find.text('Stale Name'), findsNothing);
    expect(find.text('Fresh Name'), findsWidgets);
  });

  testWidgets('switching_handle_mid_flight_drops_the_old_artists_works', (
    t,
  ) async {
    final (api, h) = await pump(t, handle: 'a');
    api.artist.single.done.complete(FakePortfolioApi.profile(handle: 'a'));
    await frames(t);
    final oldWorks = api.works.single;
    expect(oldWorks.key, 'a');
    h.router.go('/artist/b');
    await frames(t);
    api.artist.last.done.complete(FakePortfolioApi.profile(handle: 'b'));
    await frames(t);
    final bWorks = api.works.lastWhere((c) => c.key == 'b');
    bWorks.done.complete(FakePortfolioApi.workList([workJson('b-work')]));
    await frames(t);
    oldWorks.done.complete(FakePortfolioApi.workList([workJson('a-work')]));
    await frames(t);
    expect(find.text('Title a-work'), findsNothing);
    expect(find.text('Title b-work'), findsWidgets);
  });
}

/// The widget tests above never keep a list alive across a session change
/// (the screen reloads and drops it), so this checks the controller itself.
void controllerTests() {
  test(
    'works_controller_drops_a_response_from_before_a_session_change',
    () async {
      final api = FakePortfolioApi();
      final c = ProviderContainer(
        overrides: [apiProvider.overrideWithValue(api)],
      );
      addTearDown(c.dispose);
      c.listen(worksControllerProvider('ines'), (_, _) {});
      await Future<void>.delayed(Duration.zero);
      expect(api.works, hasLength(1));
      c.read(sessionEpochProvider.notifier).bump();
      await Future<void>.delayed(Duration.zero);
      expect(api.works, hasLength(2));
      api.works[1].done.complete(
        FakePortfolioApi.workList([workJson('fresh')]),
      );
      await Future<void>.delayed(Duration.zero);
      api.works[0].done.complete(
        FakePortfolioApi.workList([workJson('stale')]),
      );
      await Future<void>.delayed(Duration.zero);
      final ids = c
          .read(worksControllerProvider('ines'))
          .items
          .map((w) => w.id);
      expect(ids, ['fresh']);
    },
  );
}
