import 'package:flutter_test/flutter_test.dart';
import 'package:vela_app/features/onboarding/handle_availability.dart';
import 'package:vela_app/features/onboarding/onboarding_draft.dart';

/// S2 fix (review M-5, M-6): the onboarding name is counted and checked the
/// way the server does (code points of the cleaned text, something visible).
void main() {
  const check = HandleCheck('ana', HandleStatus.available);
  OnboardingDraft draft(String name) => OnboardingDraft(
    displayName: name,
    handle: 'ana',
    ageConfirmed: true,
    termsAccepted: true,
  );

  test('a plain name can continue', () {
    expect(draft('Ana Ruiz').canContinue(check), isTrue);
  });

  test('blank-looking names cannot continue', () {
    for (final name in ['', '   ', '⠀', '́', '​​']) {
      expect(draft(name).canContinue(check), isFalse, reason: name);
    }
  });

  test('length is code points of the cleaned name, not UTF-16 units', () {
    // 60 emoji: 120 UTF-16 units, 60 code points: allowed by the server.
    expect(draft('\u{1F600}' * 60).canContinue(check), isTrue);
    expect(draft('\u{1F600}' * 61).canContinue(check), isFalse);
    // Invisible characters the server strips do not count.
    expect(draft('​' * 10 + 'x' * 60).canContinue(check), isTrue);
  });
}
