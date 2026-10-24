/// Server limits for `GET /v1/search`: the trimmed text is 2 to 80 long
/// (UTF-16 code units, like the server's string length).
const int minSearchLength = 2;
const int maxSearchLength = 80;

/// Wait after the last keystroke before searching.
const Duration searchDebounce = Duration(milliseconds: 300);

/// [text] is already trimmed.
bool isSearchableQuery(String text) =>
    text.length >= minSearchLength && text.length <= maxSearchLength;

final RegExp _controlChars = RegExp(r'[\x00-\x08\x0B\x0C\x0E-\x1F\x7F-\x9F]');

/// [text] without control characters (the server rejects NUL; the rest are
/// never meant). Tabs and line breaks stay: they separate words.
String stripControlChars(String text) => text.replaceAll(_controlChars, '');

/// What is sent: control characters removed, then trimmed.
String cleanQuery(String text) => stripControlChars(text).trim();
