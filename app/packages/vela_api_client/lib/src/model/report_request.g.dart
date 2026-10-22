// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'report_request.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

ReportRequest _$ReportRequestFromJson(Map<String, dynamic> json) =>
    $checkedCreate('ReportRequest', json, ($checkedConvert) {
      $checkKeys(
        json,
        requiredKeys: const ['targetType', 'targetId', 'reason'],
      );
      final val = ReportRequest(
        targetType: $checkedConvert(
          'targetType',
          (v) => $enumDecode(_$ReportRequestTargetTypeEnumEnumMap, v),
        ),
        targetId: $checkedConvert('targetId', (v) => v as String),
        reason: $checkedConvert('reason', (v) => v as String),
      );
      return val;
    });

Map<String, dynamic> _$ReportRequestToJson(ReportRequest instance) =>
    <String, dynamic>{
      'targetType': _$ReportRequestTargetTypeEnumEnumMap[instance.targetType]!,
      'targetId': instance.targetId,
      'reason': instance.reason,
    };

const _$ReportRequestTargetTypeEnumEnumMap = {
  ReportRequestTargetTypeEnum.work: 'work',
  ReportRequestTargetTypeEnum.reply: 'reply',
};
