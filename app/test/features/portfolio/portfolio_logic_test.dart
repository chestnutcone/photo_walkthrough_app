import 'package:flutter_test/flutter_test.dart';
import 'package:vela_app/features/portfolio/contact_rules.dart';
import 'package:vela_app/features/portfolio/follow_toggle.dart';
import 'package:vela_app/features/portfolio/portfolio_format.dart';

void main() {
  test('compactCount_abbreviates_thousands', () {
    expect(compactCount(999), '999');
    expect(compactCount(1000), '1k');
    expect(compactCount(2400), '2.4k');
    expect(compactCount(12500), '13k');
  });

  test('followedByLine_handles_small_and_large_totals', () {
    expect(followedByLine([], 0), '');
    expect(followedByLine(['Teo Marsh'], 1), 'Followed by Teo');
    expect(followedByLine(['Teo M', 'Mara L'], 2), 'Followed by Teo and Mara');
    expect(
      followedByLine(['Teo M', 'Mara L'], 3),
      'Followed by Teo, Mara and 1 other',
    );
    expect(
      followedByLine(['Teo M', 'Mara L'], 2402),
      'Followed by Teo, Mara and 2.4k others',
    );
  });

  test('waitLabel_rounds_up_to_whole_units', () {
    expect(waitLabel(Duration.zero), '1 second');
    expect(waitLabel(const Duration(seconds: 45)), '45 seconds');
    expect(waitLabel(const Duration(seconds: 61)), '2 minutes');
    expect(waitLabel(const Duration(hours: 2)), '2 hours');
  });

  test('email_check_needs_one_at_and_no_spaces', () {
    expect(isPlausibleEmail('a@b'), isTrue);
    expect(isPlausibleEmail(' a@b.co '), isTrue);
    expect(isPlausibleEmail('@'), isFalse);
    expect(isPlausibleEmail('a@'), isFalse);
    expect(isPlausibleEmail('a b@c.d'), isFalse);
    expect(isPlausibleEmail('a@b@c'), isFalse);
    expect(isPlausibleEmail('${'x' * 250}@b.co'), isFalse);
  });

  test('message_cleaning_strips_controls_but_keeps_newlines_and_emoji', () {
    expect(
      cleanInquiryMessage('  a\u0000b\r\nc \u{1F600} שלום '),
      'ab\nc \u{1F600} שלום',
    );
    expect(cleanInquiryMessage('x' * 2500).length, kInquiryMaxMessage);
  });

  test('follower_count_adjusts_for_own_follow_only', () {
    expect(adjustedFollowerCount(5, false, null), 5);
    expect(adjustedFollowerCount(5, false, true), 6);
    expect(adjustedFollowerCount(5, true, false), 4);
    expect(adjustedFollowerCount(5, true, true), 5);
    expect(adjustedFollowerCount(0, true, false), 0);
  });
}
