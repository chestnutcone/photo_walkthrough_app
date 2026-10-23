import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:vela_app/core/api/api_providers.dart';
import 'package:vela_app/core/auth/session_store.dart';
import 'package:vela_app/features/replies/replies_controller.dart';

import '../../core/support/fakes.dart';
import 'support/fake_replies_api.dart';

const _key = (workId: 'w1', chapterIndex: 0);
final _p = repliesControllerProvider(_key);

({ProviderContainer c, FakeRepliesApi api}) _setup() {
  final api = FakeRepliesApi();
  final session = Session(
    accessToken: 'A',
    refreshToken: 'R',
    accessExpiresAt: DateTime.utc(2030),
  ).encode();
  final c = ProviderContainer(
    overrides: [
      sessionStorageProvider.overrideWithValue(FakeSessionStorage(session)),
      apiProvider.overrideWithValue(api),
    ],
  );
  addTearDown(c.dispose);
  c.listen(_p, (_, _) {});
  return (c: c, api: api);
}

List<String> _ids(ProviderContainer c) =>
    c.read(_p).items.map((r) => r.id).toList();

void main() {
  test(
    'first_page_loads_oldest_first_as_served_and_dedupes_next_page',
    () async {
      final (:c, :api) = _setup();
      await pumpEventQueue();
      api.listCalls[0].reply([reply('r1'), reply('r2')], next: 'c1');
      await pumpEventQueue();
      expect(_ids(c), ['r1', 'r2']);
      final more = c.read(_p.notifier).loadMore();
      await pumpEventQueue();
      expect(api.listCalls[1].cursor, 'c1');
      api.listCalls[1].reply([reply('r2'), reply('r3')]);
      await more;
      expect(_ids(c), ['r1', 'r2', 'r3']);
      expect(c.read(_p).nextCursor, isNull);
    },
  );

  test('stale_page_two_after_session_flip_is_ignored', () async {
    final (:c, :api) = _setup();
    await pumpEventQueue();
    api.listCalls[0].reply([reply('r1')], next: 'c1');
    await pumpEventQueue();
    final more = c.read(_p.notifier).loadMore();
    await pumpEventQueue();
    // Another account signs in: the controller rebuilds and reloads.
    await c
        .read(sessionProvider.notifier)
        .signIn(accessToken: 'B', refreshToken: 'RB', expiresIn: 3600);
    await pumpEventQueue();
    expect(api.listCalls, hasLength(3));
    api.listCalls[2].reply([reply('mine')]);
    await pumpEventQueue();
    api.listCalls[1].reply([reply('theirs')]); // abandoned, still answered
    await more;
    await pumpEventQueue();
    expect(_ids(c), ['mine']);
  });

  test('stale_first_page_after_refresh_is_ignored', () async {
    final (:c, :api) = _setup();
    await pumpEventQueue();
    final refresh = c.read(_p.notifier).refresh();
    await pumpEventQueue();
    api.listCalls[1].reply([reply('fresh')]);
    await refresh;
    api.listCalls[0].reply([reply('stale')]);
    await pumpEventQueue();
    expect(_ids(c), ['fresh']);
  });

  test('other_chapter_state_is_independent', () async {
    final (:c, :api) = _setup();
    c.listen(
      repliesControllerProvider((workId: 'w1', chapterIndex: 1)),
      (_, _) {},
    );
    await pumpEventQueue();
    expect(api.listCalls.map((x) => x.index).toSet(), {0, 1});
  });

  test('send_during_initial_load_survives_the_load', () async {
    final (:c, :api) = _setup();
    await pumpEventQueue();
    final sent = c.read(_p.notifier).send('hi');
    await pumpEventQueue();
    api.addCalls[0].succeed(reply('mine', body: 'hi'));
    expect(await sent, SendOutcome.sent);
    api.listCalls[0].reply([reply('r1')]);
    await pumpEventQueue();
    expect(_ids(c), ['r1', 'mine']);
  });

  test('send_guards_double_send_and_rejects_blank_and_overlong', () async {
    final (:c, :api) = _setup();
    await pumpEventQueue();
    api.listCalls[0].reply([]);
    await pumpEventQueue();
    final n = c.read(_p.notifier);
    expect(await n.send('   \n '), SendOutcome.ignored);
    expect(await n.send('a' * 1001), SendOutcome.ignored);
    final first = n.send('one');
    expect(await n.send('one'), SendOutcome.ignored);
    await pumpEventQueue();
    expect(api.addCalls, hasLength(1));
    expect(api.addCalls[0].body, 'one');
    api.addCalls[0].succeed(reply('r9', body: 'one'));
    expect(await first, SendOutcome.sent);
  });

  test('rate_limited_error_carries_retry_after', () async {
    final (:c, :api) = _setup();
    await pumpEventQueue();
    api.listCalls[0].reply([]);
    await pumpEventQueue();
    final out = c.read(_p.notifier).send('x');
    await pumpEventQueue();
    api.addCalls[0].fail(
      apiError('rate_limited', status: 429, retryAfter: '30'),
    );
    expect(await out, SendOutcome.failed);
    final e = c.read(_p).sendError!;
    expect(e.code, 'rate_limited');
    expect(e.retryAfter, const Duration(seconds: 30));
  });

  test('delete_removes_optimistically_and_rolls_back_on_failure', () async {
    final (:c, :api) = _setup();
    await pumpEventQueue();
    api.listCalls[0].reply([reply('r1'), reply('r2'), reply('r3')]);
    await pumpEventQueue();
    final del = c.read(_p.notifier).delete('r2');
    await pumpEventQueue();
    expect(_ids(c), ['r1', 'r3']);
    api.deleteCalls[0].fail(apiError('server_error', status: 500));
    await del;
    expect(_ids(c), ['r1', 'r2', 'r3']);
    expect(c.read(_p).actionError, isNotNull);
  });

  test('delete_rollback_is_skipped_after_session_flip', () async {
    final (:c, :api) = _setup();
    await pumpEventQueue();
    api.listCalls[0].reply([reply('r1')]);
    await pumpEventQueue();
    final del = c.read(_p.notifier).delete('r1');
    await pumpEventQueue();
    await c
        .read(sessionProvider.notifier)
        .signIn(accessToken: 'B', refreshToken: 'RB', expiresIn: 3600);
    await pumpEventQueue();
    api.listCalls[1].reply([reply('other')]);
    await pumpEventQueue();
    api.deleteCalls[0].fail(apiError('server_error', status: 500));
    await del;
    expect(_ids(c), ['other']);
  });

  test('hide_locally_removes_reply', () async {
    final (:c, :api) = _setup();
    await pumpEventQueue();
    api.listCalls[0].reply([reply('r1'), reply('r2')]);
    await pumpEventQueue();
    c.read(_p.notifier).hideLocally('r1');
    expect(_ids(c), ['r2']);
  });

  test('refresh_with_items_blocks_loadMore_with_the_stale_cursor', () async {
    final (:c, :api) = _setup();
    await pumpEventQueue();
    api.listCalls[0].reply([reply('r1')], next: 'old');
    await pumpEventQueue();
    final n = c.read(_p.notifier);
    final refresh = n.refresh();
    await pumpEventQueue();
    await n.loadMore();
    expect(api.listCalls, hasLength(2)); // only the refresh
    api.listCalls[1].reply([reply('r1'), reply('r2')]);
    await refresh;
    expect(_ids(c), ['r1', 'r2']);
  });

  test('failed_refresh_restores_the_cursor', () async {
    final (:c, :api) = _setup();
    await pumpEventQueue();
    api.listCalls[0].reply([reply('r1')], next: 'c1');
    await pumpEventQueue();
    final refresh = c.read(_p.notifier).refresh();
    await pumpEventQueue();
    api.listCalls[1].done.completeError(apiError('server_error', status: 500));
    await refresh;
    expect(c.read(_p).nextCursor, 'c1');
  });

  test('rolled_back_delete_of_a_sent_reply_survives_refresh', () async {
    final (:c, :api) = _setup();
    await pumpEventQueue();
    api.listCalls[0].reply([]);
    await pumpEventQueue();
    final n = c.read(_p.notifier);
    final sent = n.send('hi');
    await pumpEventQueue();
    api.addCalls[0].succeed(reply('mine', body: 'hi'));
    await sent;
    final del = n.delete('mine');
    await pumpEventQueue();
    api.deleteCalls[0].fail(apiError('server_error', status: 500));
    await del;
    final refresh = n.refresh();
    await pumpEventQueue();
    api.listCalls[1].reply([reply('other')]);
    await refresh;
    expect(_ids(c), ['other', 'mine']);
  });

  test('sent_reply_counts_as_own', () async {
    final (:c, :api) = _setup();
    await pumpEventQueue();
    api.listCalls[0].reply([]);
    await pumpEventQueue();
    final sent = c.read(_p.notifier).send('hi');
    await pumpEventQueue();
    api.addCalls[0].succeed(reply('mine', authorId: 'zzz'));
    await sent;
    expect(c.read(_p).ownIds, {'mine'});
  });

  test('viewer_ids_follow_the_session_flip', () async {
    final (:c, :api) = _setup();
    c.listen(viewerIdsProvider, (_, _) {});
    expect(await c.read(viewerIdsProvider.future), {'me'});
    api.meId = 'other';
    await c
        .read(sessionProvider.notifier)
        .signIn(accessToken: 'B', refreshToken: 'RB', expiresIn: 3600);
    await pumpEventQueue();
    expect(await c.read(viewerIdsProvider.future), {'other'});
  });
}
