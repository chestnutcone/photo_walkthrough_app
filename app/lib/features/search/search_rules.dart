/// Server limits for `GET /v1/search`: the trimmed text is 2 to 80 long
/// (UTF-16 code units, like the server's string length).
const int minSearchLength = 2;
const int maxSearchLength = 80;

/// Wait after the last keystroke before searching.
const Duration searchDebounce = Duration(milliseconds: 300);

/// [text] is already trimmed.
bool isSearchableQuery(String text) =>
    text.length >= minSearchLength && text.length <= maxSearchLength;
