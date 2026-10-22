// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'send_inquiry_request.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

SendInquiryRequest _$SendInquiryRequestFromJson(Map<String, dynamic> json) =>
    $checkedCreate('SendInquiryRequest', json, ($checkedConvert) {
      $checkKeys(json, requiredKeys: const ['message']);
      final val = SendInquiryRequest(
        message: $checkedConvert('message', (v) => v as String),
        workId: $checkedConvert('workId', (v) => v as String?),
      );
      return val;
    });

Map<String, dynamic> _$SendInquiryRequestToJson(SendInquiryRequest instance) =>
    <String, dynamic>{'message': instance.message, 'workId': ?instance.workId};
