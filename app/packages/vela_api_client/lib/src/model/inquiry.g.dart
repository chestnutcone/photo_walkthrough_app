// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'inquiry.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

Inquiry _$InquiryFromJson(Map<String, dynamic> json) =>
    $checkedCreate('Inquiry', json, ($checkedConvert) {
      $checkKeys(
        json,
        requiredKeys: const ['type', 'id', 'artistId', 'message', 'createdAt'],
      );
      final val = Inquiry(
        type: $checkedConvert(
          'type',
          (v) => $enumDecode(_$InquiryTypeEnumEnumMap, v),
        ),
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
  'type': _$InquiryTypeEnumEnumMap[instance.type]!,
  'id': instance.id,
  'artistId': instance.artistId,
  'workId': ?instance.workId,
  'message': instance.message,
  'createdAt': instance.createdAt.toIso8601String(),
};

const _$InquiryTypeEnumEnumMap = {
  InquiryTypeEnum.commission: 'Commission',
  InquiryTypeEnum.buyAPrint: 'Buy a print',
  InquiryTypeEnum.licensing: 'Licensing',
  InquiryTypeEnum.collaboration: 'Collaboration',
  InquiryTypeEnum.justSayingHi: 'Just saying hi',
};
