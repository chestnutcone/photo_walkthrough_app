// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'notification.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

Notification _$NotificationFromJson(Map<String, dynamic> json) =>
    $checkedCreate('Notification', json, ($checkedConvert) {
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
        createdAt: $checkedConvert(
          'createdAt',
          (v) => DateTime.parse(v as String),
        ),
        read: $checkedConvert('read', (v) => v as bool),
      );
      return val;
    });

Map<String, dynamic> _$NotificationToJson(Notification instance) =>
    <String, dynamic>{
      'id': instance.id,
      'type': _$NotificationTypeEnumEnumMap[instance.type]!,
      'actor': instance.actor.toJson(),
      'workId': ?instance.workId,
      'createdAt': instance.createdAt.toIso8601String(),
      'read': instance.read,
    };

const _$NotificationTypeEnumEnumMap = {
  NotificationTypeEnum.follow: 'follow',
  NotificationTypeEnum.reply: 'reply',
  NotificationTypeEnum.inquiry: 'inquiry',
  NotificationTypeEnum.moderation: 'moderation',
};
