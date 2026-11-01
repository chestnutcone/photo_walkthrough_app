// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'reply_envelope.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

ReplyEnvelope _$ReplyEnvelopeFromJson(Map<String, dynamic> json) =>
    $checkedCreate('ReplyEnvelope', json, ($checkedConvert) {
      $checkKeys(
        json,
        requiredKeys: const ['success', 'data', 'error', 'page'],
      );
      final val = ReplyEnvelope(
        success: $checkedConvert('success', (v) => v as bool),
        data: $checkedConvert(
          'data',
          (v) => v == null ? null : Reply.fromJson(v as Map<String, dynamic>),
        ),
        error: $checkedConvert(
          'error',
          (v) =>
              v == null ? null : ErrorBody.fromJson(v as Map<String, dynamic>),
        ),
        page: $checkedConvert(
          'page',
          (v) => v == null ? null : Page.fromJson(v as Map<String, dynamic>),
        ),
      );
      return val;
    });

Map<String, dynamic> _$ReplyEnvelopeToJson(ReplyEnvelope instance) =>
    <String, dynamic>{
      'success': instance.success,
      'data': instance.data?.toJson(),
      'error': instance.error?.toJson(),
      'page': instance.page?.toJson(),
    };
