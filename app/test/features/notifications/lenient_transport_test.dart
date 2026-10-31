import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:vela_app/core/api/api_providers.dart';
import 'package:vela_app/core/auth/session_store.dart';
import 'package:vela_app/features/notifications/notifications_controller.dart';

import '../../core/support/fakes.dart';
import 'support/fake_notifications_api.dart';

/// `Notification.type` is an open string in the contract; the controller
/// parses the page tolerantly so a type the app has never heard of (or one
/// unreadable row) never fails the whole page.
void main() {
  test('a_page_with_an_unknown_type_still_loads_the_known_rows', () async {
    final adapter = FakeAdapter(
      (o) => jsonResponse(200, {
        'success': true,
        'data': [
          notifJson('n1', type: 'follow', workId: null),
          notifJson('n2', type: 'mystery'),
          {'id': 'broken'},
        ],
        'error': null,
        'page': {'nextCursor': 'c2'},
      }),
    );
    final c = ProviderContainer(
      overrides: [
        httpClientAdapterProvider.overrideWithValue(adapter),
        sessionStorageProvider.overrideWithValue(
          FakeSessionStorage(
            Session(
              accessToken: 'A',
              refreshToken: 'R',
              accessExpiresAt: DateTime.utc(2030),
            ).encode(),
          ),
        ),
      ],
    );
    addTearDown(c.dispose);
    c.listen(notificationsControllerProvider, (_, _) {});
    for (
      var i = 0;
      i < 40 && c.read(notificationsControllerProvider).isLoading;
      i++
    ) {
      await Future<void>.delayed(const Duration(milliseconds: 25));
    }
    final s = c.read(notificationsControllerProvider);
    expect(s.error, isNull, reason: '${s.error}');
    expect(
      s.isLoading,
      isFalse,
      reason: 'loading; reqs=${adapter.requests.length}',
    );
    expect([for (final i in s.items) i.id], ['n1', 'n2']);
    expect(s.items[1].text, 'Ines a1 has an update for you');
    expect(s.nextCursor, 'c2');
    expect(s.skippedRows, 1);
    expect(adapter.requests, hasLength(1)); // one request per page
    expect(s.error, isNull);
  });

  test('a_real_server_error_is_not_masked_by_the_fallback', () async {
    final adapter = FakeAdapter(
      (o) => jsonResponse(500, errEnvelope('server_error')),
    );
    final c = ProviderContainer(
      overrides: [
        httpClientAdapterProvider.overrideWithValue(adapter),
        sessionStorageProvider.overrideWithValue(
          FakeSessionStorage(
            Session(
              accessToken: 'A',
              refreshToken: 'R',
              accessExpiresAt: DateTime.utc(2030),
            ).encode(),
          ),
        ),
      ],
    );
    addTearDown(c.dispose);
    c.listen(notificationsControllerProvider, (_, _) {});
    for (
      var i = 0;
      i < 40 && c.read(notificationsControllerProvider).isLoading;
      i++
    ) {
      await Future<void>.delayed(const Duration(milliseconds: 25));
    }
    expect(c.read(notificationsControllerProvider).error?.code, 'server_error');
    expect(adapter.requests, hasLength(1));
  });
}
