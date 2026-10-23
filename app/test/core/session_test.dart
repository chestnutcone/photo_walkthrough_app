import 'dart:convert';

import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:vela_app/core/api/api_providers.dart';
import 'package:vela_app/core/auth/auth_controller.dart';
import 'package:vela_app/core/auth/session_store.dart';

import 'support/fakes.dart';

ProviderContainer _container(FakeSessionStorage s) {
  final c = ProviderContainer(
    overrides: [
      sessionStorageProvider.overrideWithValue(s),
      clockProvider.overrideWithValue(() => DateTime.utc(2026, 10, 22, 12)),
    ],
  );
  addTearDown(c.dispose);
  return c;
}

void main() {
  test('session_survives_restart', () async {
    final storage = FakeSessionStorage();
    final first = _container(storage);
    expect(await first.read(sessionProvider.future), isNull);
    await first
        .read(sessionProvider.notifier)
        .signIn(accessToken: 'A', refreshToken: 'R', expiresIn: 900);
    first.dispose();

    final second = _container(storage);
    final restored = await second.read(sessionProvider.future);

    expect(restored?.accessToken, 'A');
    expect(restored?.refreshToken, 'R');
    expect(restored?.accessExpiresAt, DateTime.utc(2026, 10, 22, 12, 15));
  });

  test('clear removes the persisted session', () async {
    final storage = FakeSessionStorage();
    final c = _container(storage);
    await c.read(sessionProvider.future);
    await c
        .read(sessionProvider.notifier)
        .signIn(accessToken: 'A', refreshToken: 'R', expiresIn: 900);
    await c.read(sessionProvider.notifier).clear();

    expect(storage.value, isNull);
    expect(c.read(sessionProvider).value, isNull);
  });

  test('corrupt stored session is discarded', () async {
    final storage = FakeSessionStorage('{not json');
    final c = _container(storage);

    expect(await c.read(sessionProvider.future), isNull);
    expect(storage.value, isNull);
  });

  test('stored json round-trips', () {
    final s = Session(
      accessToken: 'a',
      refreshToken: 'r',
      accessExpiresAt: DateTime.utc(2026, 1, 1),
    );
    expect(jsonDecode(s.encode()), containsPair('refreshToken', 'r'));
    expect(Session.decode(s.encode()), s);
  });

  test('read failure yields signed out, not AsyncError', () async {
    final storage = FakeSessionStorage('x')..failRead = true;
    final c = _container(storage);

    expect(await c.read(sessionProvider.future), isNull);
    expect(c.read(sessionProvider).hasError, isFalse);
  });

  test('clear updates state even when delete throws', () async {
    final storage = FakeSessionStorage();
    final c = _container(storage);
    await c.read(sessionProvider.future);
    await c
        .read(sessionProvider.notifier)
        .signIn(accessToken: 'A', refreshToken: 'R', expiresIn: 900);
    storage.failDelete = true;

    await c.read(sessionProvider.notifier).clear();

    expect(c.read(sessionProvider).value, isNull);
  });

  test('signIn keeps the in-memory session when write throws', () async {
    final storage = FakeSessionStorage()..failWrite = true;
    final c = _container(storage);
    await c.read(sessionProvider.future);

    await c
        .read(sessionProvider.notifier)
        .signIn(accessToken: 'A', refreshToken: 'R', expiresIn: 900);

    expect(c.read(sessionProvider).value?.accessToken, 'A');
  });
}
