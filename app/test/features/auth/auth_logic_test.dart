import 'dart:math';

import 'package:flutter_test/flutter_test.dart';
import 'package:vela_app/core/api/api_exception.dart';
import 'package:vela_app/features/auth/auth_messages.dart';
import 'package:vela_app/features/auth/sso_client.dart';

void main() {
  group('nonce', () {
    test('apple_claim_is_lowercase_hex_sha256_of_the_raw_nonce', () {
      // The API compares the token claim with this exact form.
      expect(
        appleNonceClaim('abc'),
        'ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad',
      );
    });

    test('generated_nonces_are_unique_url_safe_and_long', () {
      final seen = {for (var i = 0; i < 50; i++) generateNonce()};
      expect(seen, hasLength(50));
      for (final n in seen) {
        expect(n.length, 43);
        expect(RegExp(r'^[A-Za-z0-9_-]+$').hasMatch(n), isTrue);
      }
    });

    test('seeded_random_is_accepted_for_determinism', () {
      expect(generateNonce(Random(1)), generateNonce(Random(1)));
    });
  });

  group('email', () {
    test('normalise_trims_and_keeps_case', () {
      expect(normalizeEmail('  Ana@Example.COM \n'), 'Ana@Example.COM');
    });

    test('looks_like_email', () {
      expect(looksLikeEmail('a@b.co'), isTrue);
      expect(looksLikeEmail(' a@b.co '), isTrue);
      for (final bad in ['', 'a', 'a@', '@b.co', 'a@b', 'a@@b.co', 'a b@c.d']) {
        expect(looksLikeEmail(bad), isFalse, reason: bad);
      }
    });
  });

  group('messages', () {
    String msg(String code, {Duration? wait}) => signInErrorMessage(
      ApiException(code: code, message: 'server text', retryAfter: wait),
    );

    test('wait_text', () {
      expect(waitText(const Duration(seconds: 20)), '20 seconds');
      expect(waitText(const Duration(seconds: 60)), '1 minute');
      expect(waitText(const Duration(seconds: 61)), '2 minutes');
      expect(waitText(null), 'a while');
    });

    test('known_codes_map_to_plain_text_without_server_text', () {
      expect(
        msg('rate_limited', wait: const Duration(minutes: 5)),
        contains('5 minutes'),
      );
      for (final code in [
        'invalid_email',
        'disposable_email',
        'invalid_code',
        'invalid_identity_token',
        'account_suspended',
        'account_deleted',
        'attestation_failed',
        'network_error',
        'something_new',
      ]) {
        expect(msg(code), isNot(contains('server text')), reason: code);
        expect(msg(code), isNotEmpty);
      }
    });
  });
}
