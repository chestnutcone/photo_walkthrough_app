// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'send_inquiry_request.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

SendInquiryRequest _$SendInquiryRequestFromJson(Map<String, dynamic> json) =>
    $checkedCreate('SendInquiryRequest', json, ($checkedConvert) {
      $checkKeys(json, requiredKeys: const ['type', 'email', 'message']);
      final val = SendInquiryRequest(
        type: $checkedConvert(
          'type',
          (v) => $enumDecode(_$SendInquiryRequestTypeEnumEnumMap, v),
        ),
        email: $checkedConvert('email', (v) => v as String),
        message: $checkedConvert('message', (v) => v as String),
        workId: $checkedConvert('workId', (v) => v as String?),
      );
      return val;
    });

Map<String, dynamic> _$SendInquiryRequestToJson(SendInquiryRequest instance) =>
    <String, dynamic>{
      'type': _$SendInquiryRequestTypeEnumEnumMap[instance.type]!,
      'email': instance.email,
      'message': instance.message,
      'workId': ?instance.workId,
    };

const _$SendInquiryRequestTypeEnumEnumMap = {
  SendInquiryRequestTypeEnum.commission: 'Commission',
  SendInquiryRequestTypeEnum.buyAPrint: 'Buy a print',
  SendInquiryRequestTypeEnum.licensing: 'Licensing',
  SendInquiryRequestTypeEnum.collaboration: 'Collaboration',
  SendInquiryRequestTypeEnum.justSayingHi: 'Just saying hi',
};
