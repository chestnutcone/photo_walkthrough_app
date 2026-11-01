import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:vela_app/core/api/api_providers.dart';
import 'package:vela_app/core/auth/session_store.dart';
import 'package:vela_app/features/feed/feed_controller.dart';
import 'package:vela_app/features/feed/follow_overrides.dart';

import '../../core/support/fakes.dart';
import 'support/fake_feed_api.dart';

final _following = feedControllerProvider(FeedTab.following);

({ProviderContainer c, FakeFeedApi api}) _setup({bool signedIn = true}) {
  final api = FakeFeedApi();
  final session = signedIn
      ? Session(
          accessToken: 'A',
          refreshToken: 'R',
          accessExpiresAt: DateTime.utc(2030),
        ).encode()
      : null;
  final c = ProviderContainer(
    overrides: [
      sessionStorageProvider.overrideWithValue(FakeSessionStorage(session)),
      apiProvider.overrideWithValue(api),
    ],
  );
  addTearDown(c.dispose);
  c.listen(_following, (_, _) {});
  c.listen(followOverridesProvider, (_, _) {});
  return (c: c, api: api);
}

Future<void> _signIn(ProviderContainer c, String token) => c
    .read(sessionProvider.notifier)
    .signIn(accessToken: token, refreshToken: 'R$token', expiresIn: 3600);

List<String> _ids(ProviderContainer c) =>
    c.read(_following).items.map((w) => w.id).toList();

void main() {
  group('stale responses (request abandoned but still answered)', () {
    test('stale_page_two_after_refresh_is_ignored', () async {
      final (:c, :api) = _setup();
      await c.read(sessionProvider.future);
      await pumpEventQueue();
      api.feedCalls[0].reply(['w1', 'w2'], next: 'c1');
      await pumpEventQueue();
      final notifier = c.read(_following.notifier);
      final more = notifier.loadMore();
      await pumpEventQueue();
      expect(api.feedCalls[1].cursor, 'c1');
      final refresh = notifier.refresh();
      await pumpEventQueue();
      api.feedCalls[2].reply(['f1']);
      await refresh;
      api.feedCalls[1].reply(['stale']);
      await more;
      await pumpEventQueue();
      expect(_ids(c), ['f1']);
      expect(c.read(_following).loadingMore, isFalse);
      expect(c.read(_following).nextCursor, isNull);
    });

    test('stale_first_page_after_refresh_in_flight_is_ignored', () async {
      final (:c, :api) = _setup();
      await c.read(sessionProvider.future);
      await pumpEventQueue();
      expect(api.feedCalls, hasLength(1)); // first page still pending
      final refresh = c.read(_following.notifier).refresh();
      await pumpEventQueue();
      expect(api.feedCalls, hasLength(2));
      api.feedCalls[1].reply(['fresh']);
      await refresh;
      api.feedCalls[0].reply(['stale']);
      await pumpEventQueue();
      expect(_ids(c), ['fresh']);
    });

    test('stale_first_page_after_session_flip_is_ignored', () async {
      final (:c, :api) = _setup(signedIn: false);
      await c.read(sessionProvider.future);
      await _signIn(c, 'A');
      await pumpEventQueue();
      await _signIn(c, 'B');
      await pumpEventQueue();
      expect(api.feedCalls, hasLength(3)); // guest, A, B
      api.feedCalls[2].reply(['b-item']);
      await pumpEventQueue();
      api.feedCalls[0].reply(['guest-item']);
      api.feedCalls[1].reply(['a-item']);
      await pumpEventQueue();
      expect(_ids(c), ['b-item']);
      expect(c.read(_following).isLoading, isFalse);
    });
  });

  group('privacy across sessions', () {
    test('sign_out_then_other_user_shows_nothing_from_the_first', () async {
      final (:c, :api) = _setup(signedIn: false);
      api.auto = (_, _) => ['a-item'];
      await c.read(sessionProvider.future);
      await _signIn(c, 'A');
      await pumpEventQueue();
      expect(_ids(c), ['a-item']);
      final toggle = c
          .read(followOverridesProvider.notifier)
          .toggle('a1', currentlyFollowing: false);
      await pumpEventQueue();
      api.followCalls.single.succeed();
      await toggle;
      expect(c.read(followOverridesProvider), {'a1': true});

      final before = api.feedCalls.length;
      api.auto = (_, _) => ['b-item'];
      await c.read(sessionProvider.notifier).clear();
      await pumpEventQueue();
      expect(c.read(followOverridesProvider), isEmpty);
      await _signIn(c, 'B');
      await pumpEventQueue();
      expect(api.feedCalls.length, greaterThan(before));
      expect(_ids(c), ['b-item']);
    });

    test('direct_switch_a_to_b_resets_feed_and_follow_overrides', () async {
      final (:c, :api) = _setup(signedIn: false);
      api.auto = (_, _) => ['a-item'];
      await c.read(sessionProvider.future);
      await _signIn(c, 'A');
      await pumpEventQueue();
      final toggle = c
          .read(followOverridesProvider.notifier)
          .toggle('a1', currentlyFollowing: false);
      await pumpEventQueue();
      api.followCalls.single.succeed();
      await toggle;
      expect(c.read(followOverridesProvider), isNotEmpty);

      api.auto = (_, _) => ['b-item'];
      await _signIn(c, 'B'); // no sign-out in between
      await pumpEventQueue();
      expect(c.read(followOverridesProvider), isEmpty);
      expect(_ids(c), ['b-item']);
    });

    test('token_refresh_does_not_reset_feed', () async {
      final (:c, :api) = _setup();
      api.auto = (_, _) => ['a-item'];
      await c.read(sessionProvider.future);
      await pumpEventQueue();
      final calls = api.feedCalls.length;
      // A refreshed token pair replaces the session object but not the user.
      c.read(sessionProvider.notifier).state = AsyncData(
        Session(
          accessToken: 'new',
          refreshToken: 'newR',
          accessExpiresAt: DateTime.utc(2031),
        ),
      );
      await pumpEventQueue();
      expect(api.feedCalls.length, calls);
      expect(_ids(c), ['a-item']);
    });

    test(
      'in_flight_follow_failing_after_account_switch_leaves_b_alone',
      () async {
        final (:c, :api) = _setup(signedIn: false);
        api.auto = (_, _) => ['a-item'];
        await c.read(sessionProvider.future);
        await _signIn(c, 'A');
        await pumpEventQueue();
        final aToggle = c
            .read(followOverridesProvider.notifier)
            .toggle('a1', currentlyFollowing: false);
        await pumpEventQueue();
        await c.read(sessionProvider.notifier).clear();
        await _signIn(c, 'B');
        await pumpEventQueue();
        expect(c.read(followOverridesProvider), isEmpty);

        api.followCalls[0].failServer();
        expect(await aToggle, FollowResult.rolledBack);
        expect(c.read(followOverridesProvider), isEmpty);

        // B's own tap on the same author is not swallowed by A's pending set.
        final bToggle = c
            .read(followOverridesProvider.notifier)
            .toggle('a1', currentlyFollowing: false);
        await pumpEventQueue();
        expect(api.followCalls, hasLength(2));
        api.followCalls[1].succeed();
        expect(await bToggle, FollowResult.done);
        expect(c.read(followOverridesProvider), {'a1': true});
      },
    );
  });

  group('follow guard', () {
    test('second_tap_while_pending_is_ignored_and_reported', () async {
      final (:c, :api) = _setup();
      await c.read(sessionProvider.future);
      final n = c.read(followOverridesProvider.notifier);
      final first = n.toggle('a1', currentlyFollowing: false);
      await pumpEventQueue();
      expect(
        await n.toggle('a1', currentlyFollowing: false),
        FollowResult.ignored,
      );
      expect(api.followCalls, hasLength(1));
      api.followCalls[0].failServer();
      expect(await first, FollowResult.rolledBack);
      expect(c.read(followOverridesProvider), {'a1': false});
    });
  });
}
