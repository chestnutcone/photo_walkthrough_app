// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'unread_count.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

UnreadCount _$UnreadCountFromJson(Map<String, dynamic> json) =>
    $checkedCreate('UnreadCount', json, ($checkedConvert) {
      $checkKeys(json, requiredKeys: const ['count']);
      final val = UnreadCount(
        count: $checkedConvert('count', (v) => (v as num).toInt()),
      );
      return val;
    });

Map<String, dynamic> _$UnreadCountToJson(UnreadCount instance) =>
    <String, dynamic>{'count': instance.count};
