// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'reply.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

Reply _$ReplyFromJson(
  Map<String, dynamic> json,
) => $checkedCreate('Reply', json, ($checkedConvert) {
  $checkKeys(
    json,
    requiredKeys: const [
      'id',
      'workId',
      'chapterIndex',
      'author',
      'body',
      'createdAt',
    ],
  );
  final val = Reply(
    id: $checkedConvert('id', (v) => v as String),
    workId: $checkedConvert('workId', (v) => v as String),
    chapterIndex: $checkedConvert('chapterIndex', (v) => (v as num).toInt()),
    author: $checkedConvert(
      'author',
      (v) => Profile.fromJson(v as Map<String, dynamic>),
    ),
    body: $checkedConvert('body', (v) => v as String),
    createdAt: $checkedConvert('createdAt', (v) => DateTime.parse(v as String)),
  );
  return val;
});

Map<String, dynamic> _$ReplyToJson(Reply instance) => <String, dynamic>{
  'id': instance.id,
  'workId': instance.workId,
  'chapterIndex': instance.chapterIndex,
  'author': instance.author.toJson(),
  'body': instance.body,
  'createdAt': instance.createdAt.toIso8601String(),
};
