import 'api/api_exception.dart';

const _maxHours = 24;
const _secondsUntilMinutes = 90;
const _minutesUntilHours = 90;

/// "Try again in N seconds." for a `Retry-After` wait. Seconds up to 89,
/// minutes up to 89, hours after that, never more than 24 hours. A missing
/// header reads as "a little while".
String tryAgainIn(Duration? wait) {
  if (wait == null) return 'Try again in a little while.';
  final seconds = wait.inMilliseconds <= 0
      ? 1
      : (wait.inMilliseconds / 1000).ceil();
  if (seconds < _secondsUntilMinutes) {
    return 'Try again in ${_count(seconds, 'second')}.';
  }
  final minutes = (seconds / 60).ceil();
  if (minutes < _minutesUntilHours) {
    return 'Try again in ${_count(minutes, 'minute')}.';
  }
  final hours = (minutes / 60).ceil();
  return 'Try again in ${_count(hours > _maxHours ? _maxHours : hours, 'hour')}.';
}

String _count(int n, String unit) => n == 1 ? '1 $unit' : '$n ${unit}s';

/// The shared message for a 429 `rate_limited`; null for any other error so
/// callers fall through to their own mapping.
String? rateLimitedMessage(ApiException e) => e.code == 'rate_limited'
    ? 'Too many requests. ${tryAgainIn(e.retryAfter)}'
    : null;
