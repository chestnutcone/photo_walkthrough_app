// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'unread_count_envelope.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

UnreadCountEnvelope _$UnreadCountEnvelopeFromJson(
  Map<String, dynamic> json,
) => $checkedCreate('UnreadCountEnvelope', json, ($checkedConvert) {
  $checkKeys(json, requiredKeys: const ['success', 'data', 'error', 'page']);
  final val = UnreadCountEnvelope(
    success: $checkedConvert('success', (v) => v as bool),
    data: $checkedConvert(
      'data',
      (v) => v == null ? null : UnreadCount.fromJson(v as Map<String, dynamic>),
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

Map<String, dynamic> _$UnreadCountEnvelopeToJson(
  UnreadCountEnvelope instance,
) => <String, dynamic>{
  'success': instance.success,
  'data': instance.data?.toJson(),
  'error': instance.error?.toJson(),
  'page': instance.page?.toJson(),
};
