import 'dart:math';

import 'package:flutter_test/flutter_test.dart';
import 'package:shared_preferences/shared_preferences.dart';
import 'package:vela_app/features/auth/sso_client.dart';

/// E1: the dev fake SSO gives each install its own account (S2-6).
void main() {
  setUp(() => SharedPreferences.setMockInitialValues({}));

  test('one_install_keeps_its_account_across_sign_ins', () async {
    final a = await DevFakeSsoClient().idToken('google', 'n');
    final b = await DevFakeSsoClient().idToken('google', 'n2');
    expect(a, b);
    expect(a, startsWith('fake:dev-google-'));
    expect(a, endsWith('@example.com'));
  });

  test('a_fresh_install_gets_a_different_account', () async {
    final first = await DevFakeSsoClient().idToken('google', 'n');
    SharedPreferences.setMockInitialValues({}); // app data cleared
    final second = await DevFakeSsoClient().idToken('google', 'n');
    expect(first, isNot(second));
  });

  test('providers_are_separate_accounts_on_one_install', () async {
    final client = DevFakeSsoClient(installId: () async => 'abc');
    expect(
      await client.idToken('google', 'n'),
      'fake:dev-google-abc:dev-google-abc@example.com',
    );
    expect(
      await client.idToken('apple', 'n'),
      'fake:dev-apple-abc:dev-apple-abc@example.com',
    );
  });

  test('install_id_is_12_hex_characters', () {
    expect(
      DevFakeSsoClient.newInstallId(Random(1)),
      matches(RegExp(r'^[0-9a-f]{12}$')),
    );
  });
}
