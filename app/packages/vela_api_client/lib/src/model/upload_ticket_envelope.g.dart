// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'upload_ticket_envelope.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

UploadTicketEnvelope _$UploadTicketEnvelopeFromJson(
  Map<String, dynamic> json,
) => $checkedCreate('UploadTicketEnvelope', json, ($checkedConvert) {
  $checkKeys(json, requiredKeys: const ['success', 'data', 'error', 'page']);
  final val = UploadTicketEnvelope(
    success: $checkedConvert('success', (v) => v as bool),
    data: $checkedConvert(
      'data',
      (v) =>
          v == null ? null : UploadTicket.fromJson(v as Map<String, dynamic>),
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

Map<String, dynamic> _$UploadTicketEnvelopeToJson(
  UploadTicketEnvelope instance,
) => <String, dynamic>{
  'success': instance.success,
  'data': instance.data?.toJson(),
  'error': instance.error?.toJson(),
  'page': instance.page?.toJson(),
};
