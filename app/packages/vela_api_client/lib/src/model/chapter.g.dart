// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'chapter.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

Chapter _$ChapterFromJson(Map<String, dynamic> json) =>
    $checkedCreate('Chapter', json, ($checkedConvert) {
      $checkKeys(json, requiredKeys: const ['x', 'y', 'title', 'body']);
      final val = Chapter(
        x: $checkedConvert('x', (v) => (v as num).toDouble()),
        y: $checkedConvert('y', (v) => (v as num).toDouble()),
        zoom: $checkedConvert('zoom', (v) => (v as num?)?.toDouble()),
        title: $checkedConvert('title', (v) => v as String),
        body: $checkedConvert('body', (v) => v as String),
        meta: $checkedConvert('meta', (v) => v as String?),
        altText: $checkedConvert('altText', (v) => v as String?),
        replyCount: $checkedConvert('replyCount', (v) => (v as num?)?.toInt()),
      );
      return val;
    });

Map<String, dynamic> _$ChapterToJson(Chapter instance) => <String, dynamic>{
  'x': instance.x,
  'y': instance.y,
  'zoom': ?instance.zoom,
  'title': instance.title,
  'body': instance.body,
  'meta': ?instance.meta,
  'altText': ?instance.altText,
  'replyCount': ?instance.replyCount,
};
