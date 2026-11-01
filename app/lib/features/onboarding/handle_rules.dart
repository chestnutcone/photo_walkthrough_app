enum HandleProblem { tooShort, tooLong, badCharacters, reserved }

/// Client-side handle format, mirroring the API's HandleRules so most
/// mistakes are caught before any request. The server stays authoritative.
abstract final class HandleRules {
  static const minLength = 3;
  static const maxLength = 20;

  /// Mirrors `HandleRules.Reserved` in the API.
  static const reserved = {
    'admin',
    'administrator',
    'support',
    'help',
    'vela',
    'moderator',
    'mod',
    'staff',
    'system',
    'root',
    'api',
    'www',
    'null',
    'undefined',
    'me',
    'artists',
    'works',
    'search',
    'notifications',
  };

  static final _chars = RegExp(r'^[a-z0-9_]+$');

  /// Trim, lower-case and drop one leading "@".
  static String normalize(String raw) {
    var h = raw.trim().toLowerCase();
    if (h.startsWith('@')) h = h.substring(1);
    return h;
  }

  /// Null for an empty field (nothing to say yet) or a valid handle.
  static HandleProblem? problem(String handle) {
    if (handle.isEmpty) return null;
    if (!_chars.hasMatch(handle)) return HandleProblem.badCharacters;
    if (handle.length < minLength) return HandleProblem.tooShort;
    if (handle.length > maxLength) return HandleProblem.tooLong;
    if (reserved.contains(handle)) return HandleProblem.reserved;
    return null;
  }

  static String message(HandleProblem p) => switch (p) {
    HandleProblem.tooShort => 'Use at least $minLength characters.',
    HandleProblem.tooLong => 'Use at most $maxLength characters.',
    HandleProblem.badCharacters =>
      'Use lowercase letters, numbers and underscores only.',
    HandleProblem.reserved => "That handle isn't available.",
  };
}
