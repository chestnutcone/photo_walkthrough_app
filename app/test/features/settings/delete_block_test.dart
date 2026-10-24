import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:vela_app/core/api/api_providers.dart';
import 'package:vela_app/core/auth/auth_controller.dart';
import 'package:vela_app/core/auth/local_data.dart';
import 'package:vela_app/core/auth/session_store.dart';
import 'package:vela_app/features/settings/delete_account_flow.dart';

import '../../core/support/fakes.dart';

final _session = Session(
  accessToken: 'A',
  refreshToken: 'R',
  accessExpiresAt: DateTime.utc(2030),
).encode();

ProviderContainer _container(String refreshCode) {
  final c = ProviderContainer(
    overrides: [
      sessionStorageProvider.overrideWithValue(FakeSessionStorage(_session)),
      wipeLocalDataProvider.overrideWithValue(() async {}),
      httpClientAdapterProvider.overrideWithValue(
        FakeAdapter(
          (o) => o.path == '/v1/auth/refresh'
              ? jsonResponse(403, errEnvelope(refreshCode))
              : jsonResponse(401, errEnvelope('unauthorized')),
        ),
      ),
    ],
  );
  addTearDown(c.dispose);
  return c;
}

void main() {
  test('deleting_never_clears_a_real_suspension_flag', () async {
    final c = _container('account_suspended');
    await c.read(deleteAccountProvider)();
    expect(c.read(accountBlockProvider), AccountBlock.suspended);
  });

  test('deleting_clears_the_account_deleted_flag', () async {
    final c = _container('account_deleted');
    await c.read(deleteAccountProvider)();
    expect(c.read(accountBlockProvider), isNull);
  });
}
