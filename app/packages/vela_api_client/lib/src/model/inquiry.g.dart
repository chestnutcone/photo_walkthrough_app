// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'inquiry.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

Inquiry _$InquiryFromJson(Map<String, dynamic> json) =>
    $checkedCreate('Inquiry', json, ($checkedConvert) {
      $checkKeys(
        json,
        requiredKeys: const ['id', 'artistId', 'message', 'createdAt'],
      );
      final val = Inquiry(
        id: $checkedConvert('id', (v) => v as String),
        artistId: $checkedConvert('artistId', (v) => v as String),
        workId: $checkedConvert('workId', (v) => v as String?),
        message: $checkedConvert('message', (v) => v as String),
        createdAt: $checkedConvert(
          'createdAt',
          (v) => DateTime.parse(v as String),
        ),
      );
      return val;
    });

Map<String, dynamic> _$InquiryToJson(Inquiry instance) => <String, dynamic>{
  'id': instance.id,
  'artistId': instance.artistId,
  'workId': ?instance.workId,
  'message': instance.message,
  'createdAt': instance.createdAt.toIso8601String(),
};
