// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'update_work_request.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

UpdateWorkRequest _$UpdateWorkRequestFromJson(Map<String, dynamic> json) =>
    $checkedCreate('UpdateWorkRequest', json, ($checkedConvert) {
      $checkKeys(json, requiredKeys: const ['title', 'chapters']);
      final val = UpdateWorkRequest(
        title: $checkedConvert('title', (v) => v as String),
        intro: $checkedConvert('intro', (v) => v as String?),
        altText: $checkedConvert('altText', (v) => v as String?),
        chapters: $checkedConvert(
          'chapters',
          (v) => (v as List<dynamic>)
              .map((e) => Chapter.fromJson(e as Map<String, dynamic>))
              .toList(),
        ),
      );
      return val;
    });

Map<String, dynamic> _$UpdateWorkRequestToJson(UpdateWorkRequest instance) =>
    <String, dynamic>{
      'title': instance.title,
      'intro': ?instance.intro,
      'altText': ?instance.altText,
      'chapters': instance.chapters.map((e) => e.toJson()).toList(),
    };
