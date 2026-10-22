// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'envelope.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

Envelope _$EnvelopeFromJson(Map<String, dynamic> json) => $checkedCreate(
  'Envelope',
  json,
  ($checkedConvert) {
    $checkKeys(json, requiredKeys: const ['success', 'data', 'error', 'page']);
    final val = Envelope(
      success: $checkedConvert('success', (v) => v as bool),
      data: $checkedConvert('data', (v) => v),
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

Map<String, dynamic> _$EnvelopeToJson(Envelope instance) => <String, dynamic>{
  'success': instance.success,
  'data': instance.data,
  'error': instance.error?.toJson(),
  'page': instance.page?.toJson(),
};
