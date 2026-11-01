// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'show_item.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

ShowItem _$ShowItemFromJson(Map<String, dynamic> json) =>
    $checkedCreate('ShowItem', json, ($checkedConvert) {
      $checkKeys(json, requiredKeys: const ['year', 'text']);
      final val = ShowItem(
        year: $checkedConvert('year', (v) => v as String),
        text: $checkedConvert('text', (v) => v as String),
      );
      return val;
    });

Map<String, dynamic> _$ShowItemToJson(ShowItem instance) => <String, dynamic>{
  'year': instance.year,
  'text': instance.text,
};
