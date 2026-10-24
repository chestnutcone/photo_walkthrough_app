/// Text redaction for crash reports. Mirrors the API's `PiiScrubber` (emails,
/// Bearer tokens, JWTs, labelled sign-in codes, SAS signatures and other
/// keyed secrets) and adds app-side rules: long digit runs and URL query
/// strings (search terms, deep-link parameters) go too.
library;

const emailMask = '[email]';
const secretMask = '[redacted]';
const numberMask = '[number]';

final _email = RegExp(r"[\w.%+\-']+@[\w\-]+(?:\.[\w\-]+)+");
final _bearer = RegExp(r'\bBearer\s+[\w.~+/\-]+=*', caseSensitive: false);
final _jwt = RegExp(r'\beyJ[\w\-]+\.[\w\-]+\.[\w\-]+');
final _code = RegExp(
  r'''(\b(?:code|otp|passcode|verification_code)["']?\s*[:=]\s*["']?)\d{6}\b''',
  caseSensitive: false,
);
final _keyedSecret = RegExp(
  r'''(\b(?:sig|signature|access_token|refresh_token|id_token|api_?key|client_secret|password|secret)["']?\s*[:=]\s*["']?)[^&\s"',;]+''',
  caseSensitive: false,
);
final _digits = RegExp(r'\b\d{6,}\b');
final _query = RegExp(r'\?[^\s#"]*');

/// Redacts personal data and credentials from free text.
String scrubText(String input) {
  var s = input;
  s = s.replaceAllMapped(_bearer, (_) => 'Bearer $secretMask');
  s = s.replaceAll(_jwt, secretMask);
  s = s.replaceAllMapped(_keyedSecret, (m) => '${m[1]}$secretMask');
  s = s.replaceAllMapped(_code, (m) => '${m[1]}$secretMask');
  s = s.replaceAll(_email, emailMask);
  return s.replaceAll(_digits, numberMask);
}

/// Like [scrubText], and drops every `?query` first: for URLs, routes and
/// navigation targets, where the query holds search text or link parameters.
String scrubUrl(String input) => scrubText(input.replaceAll(_query, ''));
