import 'package:flutter_test/flutter_test.dart';
import 'package:vela_app/core/api/api_exception.dart';
import 'package:vela_app/core/retry_text.dart';

void main() {
  test('rate_limited_error_shows_retry_after_seconds', () {
    expect(tryAgainIn(const Duration(seconds: 30)), 'Try again in 30 seconds.');
    expect(tryAgainIn(const Duration(seconds: 1)), 'Try again in 1 second.');
    expect(tryAgainIn(Duration.zero), 'Try again in 1 second.');
    expect(tryAgainIn(const Duration(seconds: 89)), 'Try again in 89 seconds.');
  });

  test('switches_to_minutes_from_90_seconds_and_hours_after_90_minutes', () {
    expect(tryAgainIn(const Duration(seconds: 90)), 'Try again in 2 minutes.');
    expect(tryAgainIn(const Duration(seconds: 121)), 'Try again in 3 minutes.');
    expect(tryAgainIn(const Duration(minutes: 89)), 'Try again in 89 minutes.');
    expect(tryAgainIn(const Duration(minutes: 90)), 'Try again in 2 hours.');
    expect(tryAgainIn(const Duration(hours: 5)), 'Try again in 5 hours.');
  });

  test('is_capped_at_24_hours_and_tolerates_a_missing_header', () {
    expect(tryAgainIn(const Duration(days: 9)), 'Try again in 24 hours.');
    expect(tryAgainIn(null), 'Try again in a little while.');
  });

  test('rateLimitedMessage_only_answers_rate_limited', () {
    const limited = ApiException(
      code: 'rate_limited',
      message: 'server text',
      statusCode: 429,
      retryAfter: Duration(seconds: 45),
    );
    expect(
      rateLimitedMessage(limited),
      'Too many requests. Try again in 45 seconds.',
    );
    expect(
      rateLimitedMessage(const ApiException(code: 'nope', message: '')),
      isNull,
    );
  });
}
