// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'create_draft_result_envelope.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

CreateDraftResultEnvelope _$CreateDraftResultEnvelopeFromJson(
  Map<String, dynamic> json,
) => $checkedCreate('CreateDraftResultEnvelope', json, ($checkedConvert) {
  $checkKeys(json, requiredKeys: const ['success', 'data', 'error', 'page']);
  final val = CreateDraftResultEnvelope(
    success: $checkedConvert('success', (v) => v as bool),
    data: $checkedConvert(
      'data',
      (v) => v == null
          ? null
          : CreateDraftResult.fromJson(v as Map<String, dynamic>),
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

Map<String, dynamic> _$CreateDraftResultEnvelopeToJson(
  CreateDraftResultEnvelope instance,
) => <String, dynamic>{
  'success': instance.success,
  'data': instance.data?.toJson(),
  'error': instance.error?.toJson(),
  'page': instance.page?.toJson(),
};
