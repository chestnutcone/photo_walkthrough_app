import 'package:flutter/foundation.dart';

/// Chapters per story are far below this; it only rejects absurd input.
const int _maxChapter = 999;

/// Where a link into a story lands: a chapter (0-based, the same index
/// notifications and replies use) and, with it, the replies sheet.
/// `/story/:id?chapter=N&replies=1`. Anything malformed is ignored, so a bad
/// link opens the story from the start.
@immutable
class StoryDeepLink {
  const StoryDeepLink({this.chapter, this.openReplies = false});

  /// The link-less default: start at the intro.
  static const none = StoryDeepLink();

  final int? chapter;
  final bool openReplies;

  factory StoryDeepLink.fromQuery(Map<String, String> query) {
    final raw = query['chapter'];
    final chapter = raw != null && RegExp(r'^\d+$').hasMatch(raw)
        ? int.tryParse(raw)
        : null;
    if (chapter == null || chapter > _maxChapter) return none;
    return StoryDeepLink(
      chapter: chapter,
      openReplies: query['replies'] == '1',
    );
  }
}

/// Route for a story, optionally at a chapter with its replies open.
String storyLocation(String workId, {int? chapter, bool openReplies = false}) {
  final base = '/story/${Uri.encodeComponent(workId)}';
  if (chapter == null) return base;
  return '$base?chapter=$chapter${openReplies ? '&replies=1' : ''}';
}

/// `/s/{workId}[?query]` (the shared link, opened by a universal / app link)
/// becomes `/story/{workId}[?query]`.
String shareLinkRedirect(Uri uri) {
  final id = uri.pathSegments.length > 1 ? uri.pathSegments[1] : '';
  final query = uri.hasQuery ? '?${uri.query}' : '';
  return '/story/${Uri.encodeComponent(id)}$query';
}
