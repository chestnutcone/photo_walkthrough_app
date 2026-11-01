import '../../core/api/api_exception.dart';
import '../../core/retry_text.dart';

/// User-facing text for a failed report. Never echoes server text or the note.
String reportErrorMessage(ApiException e) {
  if (rateLimitedMessage(e) case final limited?) return limited;
  return switch (e.code) {
    'report_limit_reached' =>
      "You've reached today's report limit. "
          '${e.retryAfter == null ? 'Try again tomorrow.' : tryAgainIn(e.retryAfter)}',
    'target_not_found' => 'This item is no longer available.',
    'cannot_report_own' => "You can't report your own content.",
    'network_error' => "Can't reach the server. Check your connection.",
    _ => 'Something went wrong. Try again.',
  };
}

/// Errors where sending the same report again cannot succeed.
bool isTerminalReportError(ApiException e) =>
    e.code == 'target_not_found' || e.code == 'cannot_report_own';
