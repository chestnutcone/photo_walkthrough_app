import '../../core/api/api_exception.dart';

/// Wrong codes the server accepts per code before it refuses the sixth
/// attempt, even a correct one (EmailCodeService.MaxAttempts).
const int maxCodeAttempts = 5;

/// Seconds before "Resend" unlocks when the server gave no cooldown.
const int defaultResendCooldownSeconds = 30;

/// The server lower-cases and trims; the app only trims, so what the user
/// typed is what they see.
String normalizeEmail(String raw) => raw.trim();

/// Cheap shape check to enable the button; the server decides validity.
bool looksLikeEmail(String raw) {
  final e = normalizeEmail(raw);
  if (e.length > 254 || e.contains(RegExp(r'\s'))) return false;
  final parts = e.split('@');
  if (parts.length != 2 || parts[0].isEmpty) return false;
  final domain = parts[1];
  return domain.contains('.') &&
      !domain.startsWith('.') &&
      !domain.endsWith('.');
}

String waitText(Duration? wait) {
  if (wait == null) return 'a while';
  final seconds = wait.inSeconds;
  if (seconds < 60) return '${seconds < 1 ? 1 : seconds} seconds';
  final minutes = (seconds / 60).ceil();
  return minutes == 1 ? '1 minute' : '$minutes minutes';
}

/// User-facing text for a failed sign-in call. Never echoes server text, the
/// email or the code.
String signInErrorMessage(ApiException e) => switch (e.code) {
  'rate_limited' => 'Too many tries. Try again in ${waitText(e.retryAfter)}.',
  'invalid_email' => 'Enter a valid email address.',
  'disposable_email' => 'Use a permanent email address.',
  'invalid_code' => "That code isn't right, or it has expired.",
  'invalid_identity_token' ||
  'unsupported_provider' => "We couldn't verify that sign-in. Try again.",
  'account_suspended' => 'This account is suspended.',
  'account_deleted' => 'This account has been deleted.',
  'attestation_required' ||
  'attestation_failed' => "This device couldn't be verified.",
  'network_error' => "Can't reach the server. Check your connection.",
  _ => 'Something went wrong. Try again.',
};
