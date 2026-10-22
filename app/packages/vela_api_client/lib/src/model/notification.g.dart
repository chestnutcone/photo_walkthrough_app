// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'notification.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

Notification _$NotificationFromJson(
  Map<String, dynamic> json,
) => $checkedCreate('Notification', json, ($checkedConvert) {
  $checkKeys(
    json,
    requiredKeys: const ['id', 'type', 'actor', 'createdAt', 'read'],
  );
  final val = Notification(
    id: $checkedConvert('id', (v) => v as String),
    type: $checkedConvert(
      'type',
      (v) => $enumDecode(_$NotificationTypeEnumEnumMap, v),
    ),
    actor: $checkedConvert(
      'actor',
      (v) => Profile.fromJson(v as Map<String, dynamic>),
    ),
    workId: $checkedConvert('workId', (v) => v as String?),
    workTitle: $checkedConvert('workTitle', (v) => v as String?),
    chapterIndex: $checkedConvert('chapterIndex', (v) => (v as num?)?.toInt()),
    replyId: $checkedConvert('replyId', (v) => v as String?),
    createdAt: $checkedConvert('createdAt', (v) => DateTime.parse(v as String)),
    read: $checkedConvert('read', (v) => v as bool),
    count: $checkedConvert('count', (v) => (v as num?)?.toInt()),
    ids: $checkedConvert(
      'ids',
      (v) => (v as List<dynamic>?)?.map((e) => e as String).toList(),
    ),
  );
  return val;
});

Map<String, dynamic> _$NotificationToJson(Notification instance) =>
    <String, dynamic>{
      'id': instance.id,
      'type': _$NotificationTypeEnumEnumMap[instance.type]!,
      'actor': instance.actor.toJson(),
      'workId': ?instance.workId,
      'workTitle': ?instance.workTitle,
      'chapterIndex': ?instance.chapterIndex,
      'replyId': ?instance.replyId,
      'createdAt': instance.createdAt.toIso8601String(),
      'read': instance.read,
      'count': ?instance.count,
      'ids': ?instance.ids,
    };

const _$NotificationTypeEnumEnumMap = {
  NotificationTypeEnum.follow: 'follow',
  NotificationTypeEnum.reply: 'reply',
  NotificationTypeEnum.newStory: 'new_story',
  NotificationTypeEnum.inquiry: 'inquiry',
  NotificationTypeEnum.moderation: 'moderation',
};
