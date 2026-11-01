// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'page.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

Page _$PageFromJson(Map<String, dynamic> json) =>
    $checkedCreate('Page', json, ($checkedConvert) {
      $checkKeys(json, requiredKeys: const ['nextCursor']);
      final val = Page(
        nextCursor: $checkedConvert('nextCursor', (v) => v as String?),
      );
      return val;
    });

Map<String, dynamic> _$PageToJson(Page instance) => <String, dynamic>{
  'nextCursor': instance.nextCursor,
};
