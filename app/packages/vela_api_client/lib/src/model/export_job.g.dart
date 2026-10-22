// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'export_job.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

ExportJob _$ExportJobFromJson(Map<String, dynamic> json) =>
    $checkedCreate('ExportJob', json, ($checkedConvert) {
      $checkKeys(json, requiredKeys: const ['id', 'status', 'createdAt']);
      final val = ExportJob(
        id: $checkedConvert('id', (v) => v as String),
        status: $checkedConvert(
          'status',
          (v) => $enumDecode(_$ExportJobStatusEnumEnumMap, v),
        ),
        downloadUrl: $checkedConvert('downloadUrl', (v) => v as String?),
        createdAt: $checkedConvert(
          'createdAt',
          (v) => DateTime.parse(v as String),
        ),
        expiresAt: $checkedConvert(
          'expiresAt',
          (v) => v == null ? null : DateTime.parse(v as String),
        ),
      );
      return val;
    });

Map<String, dynamic> _$ExportJobToJson(ExportJob instance) => <String, dynamic>{
  'id': instance.id,
  'status': _$ExportJobStatusEnumEnumMap[instance.status]!,
  'downloadUrl': ?instance.downloadUrl,
  'createdAt': instance.createdAt.toIso8601String(),
  'expiresAt': ?instance.expiresAt?.toIso8601String(),
};

const _$ExportJobStatusEnumEnumMap = {
  ExportJobStatusEnum.pending: 'pending',
  ExportJobStatusEnum.ready: 'ready',
  ExportJobStatusEnum.failed: 'failed',
};
