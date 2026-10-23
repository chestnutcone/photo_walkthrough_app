// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'submit_dmca_notice_request.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

SubmitDmcaNoticeRequest _$SubmitDmcaNoticeRequestFromJson(
  Map<String, dynamic> json,
) => $checkedCreate('SubmitDmcaNoticeRequest', json, ($checkedConvert) {
  $checkKeys(
    json,
    requiredKeys: const [
      'claimantName',
      'claimantEmail',
      'claimantAddress',
      'workReference',
      'originalWorkDescription',
      'goodFaithStatement',
      'perjuryStatement',
      'signatureName',
    ],
  );
  final val = SubmitDmcaNoticeRequest(
    claimantName: $checkedConvert('claimantName', (v) => v as String),
    claimantEmail: $checkedConvert('claimantEmail', (v) => v as String),
    claimantAddress: $checkedConvert('claimantAddress', (v) => v as String),
    claimantPhone: $checkedConvert('claimantPhone', (v) => v as String?),
    workReference: $checkedConvert('workReference', (v) => v as String),
    originalWorkDescription: $checkedConvert(
      'originalWorkDescription',
      (v) => v as String,
    ),
    goodFaithStatement: $checkedConvert('goodFaithStatement', (v) => v as bool),
    perjuryStatement: $checkedConvert('perjuryStatement', (v) => v as bool),
    signatureName: $checkedConvert('signatureName', (v) => v as String),
  );
  return val;
});

Map<String, dynamic> _$SubmitDmcaNoticeRequestToJson(
  SubmitDmcaNoticeRequest instance,
) => <String, dynamic>{
  'claimantName': instance.claimantName,
  'claimantEmail': instance.claimantEmail,
  'claimantAddress': instance.claimantAddress,
  'claimantPhone': ?instance.claimantPhone,
  'workReference': instance.workReference,
  'originalWorkDescription': instance.originalWorkDescription,
  'goodFaithStatement': instance.goodFaithStatement,
  'perjuryStatement': instance.perjuryStatement,
  'signatureName': instance.signatureName,
};
