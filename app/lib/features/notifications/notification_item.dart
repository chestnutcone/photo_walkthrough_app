import 'package:flutter/foundation.dart';
import 'package:vela_api_client/vela_api_client.dart' as api;

import '../story/story_deeplink.dart';

/// Kinds the app knows how to word. Anything else is [unknown], which gets
/// generic text instead of failing the list.
enum NotificationKind {
  follow('follow'),
  reply('reply'),
  newStory('new_story'),
  inquiry('inquiry'),
  moderation('moderation'),
  unknown('');

  const NotificationKind(this.wire);
  final String wire;

  static NotificationKind fromWire(Object? value) {
    for (final k in values) {
      if (k != unknown && k.wire == value) return k;
    }
    return unknown;
  }
}

/// One row of the notifications list: the app's own immutable view of
/// the contract's `Notification`, tolerant of types added later.
@immutable
class NotificationItem {
  const NotificationItem({
    required this.id,
    required this.ids,
    required this.kind,
    required this.actor,
    required this.createdAt,
    required this.read,
    required this.count,
    this.workId,
    this.workTitle,
    this.chapterIndex,
  });

  /// The latest row's id (what the list keys on).
  final String id;

  /// Every row folded into this item; what `markRead` is sent.
  final List<String> ids;
  final NotificationKind kind;
  final api.Profile actor;
  final DateTime createdAt;
  final bool read;
  final int count;
  final String? workId;
  final String? workTitle;
  final int? chapterIndex;

  /// Parses one raw JSON row; null when it lacks what a row needs (id,
  /// actor, date). An unknown `type` is fine.
  static NotificationItem? tryFromJson(Object? json) {
    if (json is! Map) return null;
    try {
      final id = json['id'];
      final createdAt = json['createdAt'];
      final actor = json['actor'];
      if (id is! String || createdAt is! String || actor is! Map) return null;
      final count = json['count'];
      final chapter = json['chapterIndex'];
      final rawIds = json['ids'];
      return NotificationItem(
        id: id,
        ids: _idsOf(
          id,
          rawIds is List ? rawIds.whereType<String>().toList() : null,
        ),
        kind: NotificationKind.fromWire(json['type']),
        actor: api.Profile.fromJson(Map<String, dynamic>.from(actor)),
        createdAt: DateTime.parse(createdAt),
        read: json['read'] == true,
        count: count is int && count > 0 ? count : 1,
        workId: json['workId'] is String ? json['workId'] as String : null,
        workTitle: json['workTitle'] is String
            ? json['workTitle'] as String
            : null,
        chapterIndex: chapter is int ? chapter : null,
      );
    } on Object {
      // A row the app cannot read is skipped, never fatal.
      return null;
    }
  }

  NotificationItem copyWith({bool? read}) => NotificationItem(
    id: id,
    ids: ids,
    kind: kind,
    actor: actor,
    createdAt: createdAt,
    read: read ?? this.read,
    count: count,
    workId: workId,
    workTitle: workTitle,
    chapterIndex: chapterIndex,
  );

  String get _actorName => actor.displayName;
  String get _story => workTitle ?? 'a story';

  /// The line shown in the list.
  String get text => switch (kind) {
    NotificationKind.follow => '$_actorName started following you',
    NotificationKind.reply when count > 1 => '$count new replies on $_story',
    NotificationKind.reply => '$_actorName replied on $_story',
    NotificationKind.newStory => '$_actorName published $_story',
    NotificationKind.inquiry => '$_actorName sent you an inquiry',
    NotificationKind.moderation => 'A moderator reviewed your story',
    NotificationKind.unknown => '$_actorName has an update for you',
  };

  /// Where tapping goes, or null when there is nowhere sensible.
  String? get location {
    final work = workId;
    final artist = actor.handle.isEmpty
        ? null
        : '/artist/${Uri.encodeComponent(actor.handle)}';
    return switch (kind) {
      NotificationKind.follow ||
      NotificationKind.inquiry ||
      NotificationKind.unknown => artist,
      NotificationKind.reply =>
        work == null
            ? null
            : storyLocation(
                work,
                chapter: chapterIndex,
                openReplies: chapterIndex != null,
              ),
      NotificationKind.newStory ||
      NotificationKind.moderation => work == null ? null : storyLocation(work),
    };
  }
}

List<String> _idsOf(String id, List<String>? ids) =>
    List.unmodifiable(ids == null || ids.isEmpty ? [id] : ids);

/// Rows of a raw `data` array that the app can use, in order, and how many
/// it had to skip.
({List<NotificationItem> items, int skipped}) parseNotificationRows(
  Object? data,
) {
  final items = <NotificationItem>[];
  var skipped = 0;
  if (data is List) {
    for (final row in data) {
      final item = NotificationItem.tryFromJson(row);
      if (item == null) {
        skipped++;
      } else {
        items.add(item);
      }
    }
  }
  return (items: items, skipped: skipped);
}

/// "now", "5m", "3h", "2d", "2w", "1y": how long ago, compactly.
String relativeTime(DateTime then, DateTime now) {
  final d = now.difference(then);
  if (d.inSeconds < 60) return 'now';
  if (d.inMinutes < 60) return '${d.inMinutes}m';
  if (d.inHours < 24) return '${d.inHours}h';
  if (d.inDays < 7) return '${d.inDays}d';
  if (d.inDays < 365) return '${d.inDays ~/ 7}w';
  return '${d.inDays ~/ 365}y';
}
