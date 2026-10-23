import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:vela_app/core/api/api_providers.dart';
import 'package:vela_app/core/auth/local_data.dart';
import 'package:vela_app/core/auth/session_store.dart';
import 'package:vela_app/features/auth/sign_out.dart';

import 'support/fakes.dart';

void main() {
  ProviderContainer make(FakeSessionStorage storage) {
    final c = ProviderContainer(
      overrides: [sessionStorageProvider.overrideWithValue(storage)],
    );
    addTearDown(c.dispose);
    return c;
  }

  final session = Session(
    accessToken: 'A',
    refreshToken: 'R',
    accessExpiresAt: DateTime.utc(2030),
  ).encode();

  test('features_register_cleaners_without_editing_local_data', () async {
    final c = make(FakeSessionStorage(session));
    final ran = <String>[];
    // Registered from a feature's own provider, as B11 will do.
    final featureProvider = Provider<int>((ref) {
      registerLocalDataCleaner(
        ref,
        'recent-searches',
        () async => ran.add('searches'),
      );
      return 1;
    });
    c.read(featureProvider);
    await c.read(wipeLocalDataProvider)();
    expect(ran, ['searches']);
  });

  test('registering_the_same_name_twice_replaces_it', () async {
    final c = make(FakeSessionStorage(session));
    final ran = <String>[];
    final reg = c.read(localDataRegistryProvider);
    reg.register('x', () async => ran.add('old'));
    reg.register('x', () async => ran.add('new'));
    await c.read(wipeLocalDataProvider)();
    expect(ran, ['new']);
  });

  test(
    'a_failing_cleaner_does_not_stop_the_others_or_the_session_clear',
    () async {
      final storage = FakeSessionStorage(session);
      final c = make(storage);
      final ran = <String>[];
      final reg = c.read(localDataRegistryProvider);
      reg.register('a', () async => ran.add('a'));
      reg.register('boom', () async => throw StateError('disk'));
      reg.register('c', () async => ran.add('c'));
      await c.read(sessionProvider.future);
      await c.read(wipeLocalDataProvider)();
      expect(ran, ['a', 'c']);
      expect(storage.value, isNull);
      expect(c.read(sessionProvider).value, isNull);
    },
  );

  test('sign_out_clears_the_session_only_and_leaves_cleaners_alone', () async {
    final storage = FakeSessionStorage(session);
    final c = make(storage);
    final ran = <String>[];
    c.read(localDataRegistryProvider).register('a', () async => ran.add('a'));
    await c.read(sessionProvider.future);
    await c.read(signOutProvider)();
    expect(ran, isEmpty);
    expect(storage.value, isNull);
  });
}
