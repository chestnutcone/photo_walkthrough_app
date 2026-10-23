import 'package:flutter_test/flutter_test.dart';
import 'package:vela_app/features/feed/feed_format.dart';

void main() {
  test('formats dates without year in the current year', () {
    final now = DateTime(2026, 10, 23);
    expect(formatFeedDate(DateTime(2026, 10, 2), now), 'Oct 2');
    expect(formatFeedDate(DateTime(2025, 1, 15), now), 'Jan 15, 2025');
  });

  test('caps unread label at 99+', () {
    expect(unreadLabel(99), '99');
    expect(unreadLabel(100), '99+');
  });
}
