// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'dmca_notice_ack_envelope.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

DmcaNoticeAckEnvelope _$DmcaNoticeAckEnvelopeFromJson(
  Map<String, dynamic> json,
) => $checkedCreate('DmcaNoticeAckEnvelope', json, ($checkedConvert) {
  $checkKeys(json, requiredKeys: const ['success', 'data', 'error', 'page']);
  final val = DmcaNoticeAckEnvelope(
    success: $checkedConvert('success', (v) => v as bool),
    data: $checkedConvert(
      'data',
      (v) =>
          v == null ? null : DmcaNoticeAck.fromJson(v as Map<String, dynamic>),
    ),
    error: $checkedConvert(
      'error',
      (v) => v == null ? null : ErrorBody.fromJson(v as Map<String, dynamic>),
    ),
    page: $checkedConvert(
      'page',
      (v) => v == null ? null : Page.fromJson(v as Map<String, dynamic>),
    ),
  );
  return val;
});

Map<String, dynamic> _$DmcaNoticeAckEnvelopeToJson(
  DmcaNoticeAckEnvelope instance,
) => <String, dynamic>{
  'success': instance.success,
  'data': instance.data?.toJson(),
  'error': instance.error?.toJson(),
  'page': instance.page?.toJson(),
};
