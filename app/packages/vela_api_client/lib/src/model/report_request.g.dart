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
        reason: $checkedConvert(
          'reason',
          (v) => $enumDecode(_$ReportRequestReasonEnumEnumMap, v),
        ),
        note: $checkedConvert('note', (v) => v as String?),
      );
      return val;
    });

Map<String, dynamic> _$ReportRequestToJson(ReportRequest instance) =>
    <String, dynamic>{
      'targetType': _$ReportRequestTargetTypeEnumEnumMap[instance.targetType]!,
      'targetId': instance.targetId,
      'reason': _$ReportRequestReasonEnumEnumMap[instance.reason]!,
      'note': ?instance.note,
    };

const _$ReportRequestTargetTypeEnumEnumMap = {
  ReportRequestTargetTypeEnum.work: 'work',
  ReportRequestTargetTypeEnum.reply: 'reply',
  ReportRequestTargetTypeEnum.user: 'user',
};

const _$ReportRequestReasonEnumEnumMap = {
  ReportRequestReasonEnum.illegalContent: 'illegal_content',
  ReportRequestReasonEnum.copyright: 'copyright',
  ReportRequestReasonEnum.harassment: 'harassment',
  ReportRequestReasonEnum.spam: 'spam',
  ReportRequestReasonEnum.other: 'other',
};
