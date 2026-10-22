// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'work.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

Work _$WorkFromJson(Map<String, dynamic> json) => $checkedCreate('Work', json, (
  $checkedConvert,
) {
  $checkKeys(
    json,
    requiredKeys: const [
      'id',
      'author',
      'title',
      'status',
      'chapters',
      'createdAt',
      'shareUrl',
    ],
  );
  final val = Work(
    id: $checkedConvert('id', (v) => v as String),
    author: $checkedConvert(
      'author',
      (v) => Profile.fromJson(v as Map<String, dynamic>),
    ),
    title: $checkedConvert('title', (v) => v as String),
    status: $checkedConvert(
      'status',
      (v) => $enumDecode(_$WorkStatusEnumEnumMap, v),
    ),
    imageUrl: $checkedConvert('imageUrl', (v) => v as String?),
    imageWidth: $checkedConvert('imageWidth', (v) => (v as num?)?.toInt()),
    imageHeight: $checkedConvert('imageHeight', (v) => (v as num?)?.toInt()),
    chapters: $checkedConvert(
      'chapters',
      (v) => (v as List<dynamic>)
          .map((e) => Chapter.fromJson(e as Map<String, dynamic>))
          .toList(),
    ),
    createdAt: $checkedConvert('createdAt', (v) => DateTime.parse(v as String)),
    publishedAt: $checkedConvert(
      'publishedAt',
      (v) => v == null ? null : DateTime.parse(v as String),
    ),
    draftExpiresAt: $checkedConvert(
      'draftExpiresAt',
      (v) => v == null ? null : DateTime.parse(v as String),
    ),
    shareUrl: $checkedConvert('shareUrl', (v) => v as String),
  );
  return val;
});

Map<String, dynamic> _$WorkToJson(Work instance) => <String, dynamic>{
  'id': instance.id,
  'author': instance.author.toJson(),
  'title': instance.title,
  'status': _$WorkStatusEnumEnumMap[instance.status]!,
  'imageUrl': ?instance.imageUrl,
  'imageWidth': ?instance.imageWidth,
  'imageHeight': ?instance.imageHeight,
  'chapters': instance.chapters.map((e) => e.toJson()).toList(),
  'createdAt': instance.createdAt.toIso8601String(),
  'publishedAt': ?instance.publishedAt?.toIso8601String(),
  'draftExpiresAt': ?instance.draftExpiresAt?.toIso8601String(),
  'shareUrl': instance.shareUrl,
};

const _$WorkStatusEnumEnumMap = {
  WorkStatusEnum.draft: 'draft',
  WorkStatusEnum.pending: 'pending',
  WorkStatusEnum.published: 'published',
  WorkStatusEnum.blocked: 'blocked',
  WorkStatusEnum.removed: 'removed',
};
