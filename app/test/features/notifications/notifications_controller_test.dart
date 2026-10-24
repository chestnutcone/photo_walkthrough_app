import 'package:flutter/foundation.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:vela_app/core/api/api_providers.dart';
import 'package:vela_app/core/auth/session_store.dart';
import 'package:vela_app/features/notifications/notifications_controller.dart';
import 'package:vela_app/features/notifications/unread_count_provider.dart';

import '../../core/support/fakes.dart';
import 'support/fake_notifications_api.dart';

final _session = Session(
  accessToken: 'A',
  refreshToken: 'R',
  accessExpiresAt: DateTime.utc(2030),
).encode();

class _Rig {
  _Rig({bool signedIn = true}) {
    container = ProviderContainer(
      overrides: [
        apiProvider.overrideWithValue(api),
        notificationsFetcherProvider.overrideWithValue(api.fetchList),
        sessionStorageProvider.overrideWithValue(
          FakeSessionStorage(signedIn ? _session : null),
        ),
      ],
    );
    container.listen(notificationsControllerProvider, (_, _) {});
  }

  final api = FakeNotificationsApi();
  late final ProviderContainer container;

  NotificationsController get ctl =>
      container.read(notificationsControllerProvider.notifier);
  NotificationsState get state =>
      container.read(notificationsControllerProvider);
  List<String> get ids => [for (final i in state.items) i.id];
}

Future<void> _tick() => Future<void>.delayed(Duration.zero);

Future<_Rig> _loaded(List<Map<String, Object?>> items, {String? next}) async {
  final rig = _Rig();
  await _tick();
  rig.api.listCalls.single.reply(items, next: next);
  await _tick();
  return rig;
}

void main() {
  malformed();
  test('opening_loads_the_first_page', () async {
    final rig = _Rig();
    expect(rig.state.isLoading, isTrue);
    await _tick();
    expect(rig.api.listCalls.single.cursor, isNull);
    rig.api.listCalls.single.reply([notifJson('n1'), notifJson('n2')]);
    await _tick();
    expect(rig.ids, ['n1', 'n2']);
    expect(rig.state.isLoading, isFalse);
    rig.container.dispose();
  });

  test('a_guest_session_sends_no_request', () async {
    final rig = _Rig(signedIn: false);
    await _tick();
    await _tick();
    expect(rig.api.listCalls, isEmpty);
    rig.container.dispose();
  });

  test('first_load_failure_shows_an_error_and_retry_loads_again', () async {
    final rig = _Rig();
    await _tick();
    rig.api.listCalls[0].fail();
    await _tick();
    expect(rig.state.error, isNotNull);
    expect(rig.state.items, isEmpty);
    rig.ctl.retry();
    await _tick();
    rig.api.listCalls[1].reply([notifJson('n1')]);
    await _tick();
    expect(rig.state.error, isNull);
    expect(rig.ids, ['n1']);
    rig.container.dispose();
  });

  test('stale_refresh_answer_is_ignored', () async {
    final rig = await _loaded([notifJson('old')]);
    final first = rig.ctl.refresh();
    await _tick();
    final second = rig.ctl.refresh();
    await _tick();
    rig.api.listCalls[2].reply([notifJson('newer')]);
    await second;
    // The abandoned refresh answers last; the fake ignores cancellation.
    rig.api.listCalls[1].reply([notifJson('stale')]);
    await first;
    expect(rig.ids, ['newer']);
    expect(rig.state.refreshing, isFalse);
    rig.container.dispose();
  });

  test('refresh_keeps_items_visible_and_replaces_them', () async {
    final rig = await _loaded([notifJson('n1')], next: 'c1');
    final f = rig.ctl.refresh();
    await _tick();
    expect(rig.state.refreshing, isTrue);
    expect(rig.ids, ['n1']);
    expect(rig.api.listCalls[1].cursor, isNull);
    rig.api.listCalls[1].reply([notifJson('n9'), notifJson('n1')]);
    await f;
    expect(rig.ids, ['n9', 'n1']);
    expect(rig.state.nextCursor, isNull);
    rig.container.dispose();
  });

  test('refresh_failure_keeps_items_and_reports_it', () async {
    final rig = await _loaded([notifJson('n1')]);
    final f = rig.ctl.refresh();
    await _tick();
    rig.api.listCalls[1].fail();
    await f;
    expect(rig.ids, ['n1']);
    expect(rig.state.refreshError, isNotNull);
    expect(rig.state.error, isNull);
    rig.container.dispose();
  });

  test('pagination_sends_the_cursor_and_skips_duplicates', () async {
    final rig = await _loaded([notifJson('n1')], next: 'c1');
    rig.ctl.loadMore();
    rig.ctl.loadMore(); // already running
    await _tick();
    expect(rig.api.listCalls, hasLength(2));
    expect(rig.api.listCalls[1].cursor, 'c1');
    rig.api.listCalls[1].reply([notifJson('n1'), notifJson('n2')]);
    await _tick();
    expect(rig.ids, ['n1', 'n2']);
    rig.ctl.loadMore();
    await _tick();
    expect(rig.api.listCalls, hasLength(2));
    rig.container.dispose();
  });

  test('a_page_requested_before_a_refresh_is_dropped', () async {
    final rig = await _loaded([notifJson('n1')], next: 'c1');
    rig.ctl.loadMore();
    await _tick();
    final page = rig.api.listCalls[1];
    final f = rig.ctl.refresh();
    await _tick();
    rig.api.listCalls[2].reply([notifJson('fresh')]);
    await f;
    page.reply([notifJson('stale-page')], next: 'x');
    await _tick();
    expect(rig.ids, ['fresh']);
    expect(rig.state.nextCursor, isNull);
    expect(rig.state.loadingMore, isFalse);
    rig.container.dispose();
  });

  test(
    'a_later_page_failure_keeps_items_and_retry_reuses_the_cursor',
    () async {
      final rig = await _loaded([notifJson('n1')], next: 'c1');
      rig.ctl.loadMore();
      await _tick();
      rig.api.listCalls[1].fail();
      await _tick();
      expect(rig.state.moreError, isNotNull);
      expect(rig.ids, ['n1']);
      rig.ctl.retryMore();
      await _tick();
      expect(rig.api.listCalls[2].cursor, 'c1');
      rig.api.listCalls[2].reply([notifJson('n2')]);
      await _tick();
      expect(rig.ids, ['n1', 'n2']);
      rig.container.dispose();
    },
  );

  test('session_flip_drops_the_old_users_answer_and_reloads', () async {
    final rig = _Rig();
    await _tick();
    final before = rig.api.listCalls[0];
    rig.container.read(sessionEpochProvider.notifier).bump();
    await _tick();
    await _tick();
    expect(rig.api.listCalls, hasLength(2));
    rig.api.listCalls[1].reply([notifJson('after')]);
    await _tick();
    before.reply([notifJson('before')]);
    await _tick();
    expect(rig.ids, ['after']);
    rig.container.dispose();
  });

  test('session_flip_drops_the_old_users_items_at_once', () async {
    final rig = await _loaded([notifJson('mine')]);
    rig.container.read(sessionEpochProvider.notifier).bump();
    await _tick();
    expect(rig.state.items, isEmpty);
    rig.container.dispose();
  });

  group('markRead', () {
    test(
      'is_optimistic_sends_every_folded_id_and_refreshes_the_badge',
      () async {
        final rig = await _loaded([
          notifJson('n3', count: 3, ids: ['n3', 'n2', 'n1']),
        ]);
        rig.api.unread = 3;
        final badge = rig.container.listen(unreadCountProvider, (_, _) {});
        await rig.container.read(unreadCountProvider.future);
        expect(badge.read().value, 3);
        final f = rig.ctl.markRead(rig.state.items.single);
        await _tick();
        expect(rig.state.items.single.read, isTrue);
        expect(rig.api.markCalls.single.ids, ['n3', 'n2', 'n1']);
        rig.api.unread = 0;
        rig.api.markCalls.single.succeed();
        expect(await f, isTrue);
        await rig.container.read(unreadCountProvider.future);
        expect(badge.read().value, 0);
        rig.container.dispose();
      },
    );

    test('rolls_back_on_failure', () async {
      final rig = await _loaded([notifJson('n1')]);
      final f = rig.ctl.markRead(rig.state.items.single);
      await _tick();
      rig.api.markCalls.single.fail();
      expect(await f, isFalse);
      expect(rig.state.items.single.read, isFalse);
      rig.container.dispose();
    });

    test('an_already_read_item_sends_nothing', () async {
      final rig = await _loaded([notifJson('n1', read: true)]);
      expect(await rig.ctl.markRead(rig.state.items.single), isTrue);
      expect(rig.api.markCalls, isEmpty);
      rig.container.dispose();
    });

    test(
      'a_refresh_during_the_call_does_not_show_the_item_unread_again',
      () async {
        final rig = await _loaded([notifJson('n1')]);
        final f = rig.ctl.markRead(rig.state.items.single);
        await _tick();
        final r = rig.ctl.refresh();
        await _tick();
        rig.api.listCalls[1].reply([notifJson('n1')]); // server not caught up
        await r;
        expect(rig.state.items.single.read, isTrue);
        rig.api.markCalls.single.succeed();
        await f;
        rig.container.dispose();
      },
    );

    test('rollback_after_a_failed_call_survives_a_refresh', () async {
      final rig = await _loaded([notifJson('n1')]);
      final f = rig.ctl.markRead(rig.state.items.single);
      await _tick();
      rig.api.markCalls.single.fail();
      await f;
      final r = rig.ctl.refresh();
      await _tick();
      rig.api.listCalls[1].reply([notifJson('n1')]);
      await r;
      expect(rig.state.items.single.read, isFalse);
      rig.container.dispose();
    });

    test(
      'a_failure_after_a_session_flip_does_not_touch_the_new_users_list',
      () async {
        final rig = await _loaded([notifJson('n1')]);
        final f = rig.ctl.markRead(rig.state.items.single);
        await _tick();
        rig.container.read(sessionEpochProvider.notifier).bump();
        await _tick();
        await _tick();
        rig.api.listCalls[1].reply([notifJson('n1', read: true)]); // other user
        await _tick();
        rig.api.markCalls.single.fail();
        await f;
        expect(rig.state.items.single.read, isTrue);
        rig.container.dispose();
      },
    );

    test('completing_after_dispose_is_safe', () async {
      final rig = await _loaded([notifJson('n1')]);
      final f = rig.ctl.markRead(rig.state.items.single);
      await _tick();
      rig.container.dispose();
      rig.api.markCalls.single.succeed();
      await f;
    });
  });

  test('responses_after_dispose_are_ignored', () async {
    final rig = _Rig();
    await _tick();
    final call = rig.api.listCalls.single;
    rig.container.dispose();
    call.reply([notifJson('n1')]);
    await _tick();
  });
}

void malformed() {
  test('a_malformed_200_body_is_an_error_not_an_empty_list', () async {
    for (final body in <Object?>[
      null,
      'html',
      {'success': true, 'data': null, 'error': null, 'page': null},
      {
        'success': true,
        'data': {'not': 'a list'},
      },
      {
        'success': false,
        'data': null,
        'error': {'code': 'x', 'message': 'm'},
      },
    ]) {
      final rig = _Rig();
      await _tick();
      rig.api.listCalls.single.replyRaw(body);
      await _tick();
      expect(rig.state.error, isNotNull, reason: '$body');
      expect(rig.state.items, isEmpty);
      rig.container.dispose();
    }
  });

  test('skipped_rows_are_counted_and_logged_by_count_only', () async {
    final logs = <String>[];
    final old = debugPrint;
    debugPrint = (m, {wrapWidth}) => logs.add(m ?? '');
    try {
      final rig = _Rig();
      await _tick();
      rig.api.listCalls.single.reply([
        notifJson('ok', title: 'secret-title'),
        {'id': 'broken', 'secret': 'secret-title'},
      ]);
      await _tick();
      expect(rig.ids, ['ok']);
      expect(rig.state.skippedRows, 1);
      expect(logs.join(), contains('1'));
      expect(logs.where((l) => l.contains('secret')), isEmpty);
      rig.container.dispose();
    } finally {
      debugPrint = old;
    }
  });
}
