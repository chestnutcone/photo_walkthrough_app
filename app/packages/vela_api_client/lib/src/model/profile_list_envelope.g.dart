// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'profile_list_envelope.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

ProfileListEnvelope _$ProfileListEnvelopeFromJson(Map<String, dynamic> json) =>
    $checkedCreate('ProfileListEnvelope', json, ($checkedConvert) {
      $checkKeys(
        json,
        requiredKeys: const ['success', 'data', 'error', 'page'],
      );
      final val = ProfileListEnvelope(
        success: $checkedConvert('success', (v) => v as bool),
        data: $checkedConvert(
          'data',
          (v) => (v as List<dynamic>)
              .map((e) => Profile.fromJson(e as Map<String, dynamic>))
              .toList(),
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

Map<String, dynamic> _$ProfileListEnvelopeToJson(
  ProfileListEnvelope instance,
) => <String, dynamic>{
  'success': instance.success,
  'data': instance.data.map((e) => e.toJson()).toList(),
  'error': instance.error?.toJson(),
  'page': instance.page?.toJson(),
};
