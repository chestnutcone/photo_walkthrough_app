// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'create_draft_result.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

CreateDraftResult _$CreateDraftResultFromJson(Map<String, dynamic> json) =>
    $checkedCreate('CreateDraftResult', json, ($checkedConvert) {
      $checkKeys(json, requiredKeys: const ['workId', 'uploadUrl']);
      final val = CreateDraftResult(
        workId: $checkedConvert('workId', (v) => v as String),
        uploadUrl: $checkedConvert('uploadUrl', (v) => v as String),
      );
      return val;
    });

Map<String, dynamic> _$CreateDraftResultToJson(CreateDraftResult instance) =>
    <String, dynamic>{
      'workId': instance.workId,
      'uploadUrl': instance.uploadUrl,
    };
