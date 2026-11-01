// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'me_envelope.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

MeEnvelope _$MeEnvelopeFromJson(Map<String, dynamic> json) => $checkedCreate(
  'MeEnvelope',
  json,
  ($checkedConvert) {
    $checkKeys(json, requiredKeys: const ['success', 'data', 'error', 'page']);
    final val = MeEnvelope(
      success: $checkedConvert('success', (v) => v as bool),
      data: $checkedConvert(
        'data',
        (v) => v == null ? null : Me.fromJson(v as Map<String, dynamic>),
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
  },
);

Map<String, dynamic> _$MeEnvelopeToJson(MeEnvelope instance) =>
    <String, dynamic>{
      'success': instance.success,
      'data': instance.data?.toJson(),
      'error': instance.error?.toJson(),
      'page': instance.page?.toJson(),
    };
