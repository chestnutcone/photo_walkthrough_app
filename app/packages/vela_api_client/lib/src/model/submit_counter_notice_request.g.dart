// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'submit_counter_notice_request.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

SubmitCounterNoticeRequest _$SubmitCounterNoticeRequestFromJson(
  Map<String, dynamic> json,
) => $checkedCreate('SubmitCounterNoticeRequest', json, ($checkedConvert) {
  $checkKeys(
    json,
    requiredKeys: const [
      'name',
      'address',
      'statement',
      'consentToJurisdiction',
      'goodFaithBelief',
    ],
  );
  final val = SubmitCounterNoticeRequest(
    name: $checkedConvert('name', (v) => v as String),
    address: $checkedConvert('address', (v) => v as String),
    phone: $checkedConvert('phone', (v) => v as String?),
    statement: $checkedConvert('statement', (v) => v as String),
    consentToJurisdiction: $checkedConvert(
      'consentToJurisdiction',
      (v) => v as bool,
    ),
    goodFaithBelief: $checkedConvert('goodFaithBelief', (v) => v as bool),
  );
  return val;
});

Map<String, dynamic> _$SubmitCounterNoticeRequestToJson(
  SubmitCounterNoticeRequest instance,
) => <String, dynamic>{
  'name': instance.name,
  'address': instance.address,
  'phone': ?instance.phone,
  'statement': instance.statement,
  'consentToJurisdiction': instance.consentToJurisdiction,
  'goodFaithBelief': instance.goodFaithBelief,
};
