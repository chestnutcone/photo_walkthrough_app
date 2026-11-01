// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'me_tier.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

MeTier _$MeTierFromJson(Map<String, dynamic> json) =>
    $checkedCreate('MeTier', json, ($checkedConvert) {
      $checkKeys(json, requiredKeys: const ['name', 'maxWorks']);
      final val = MeTier(
        name: $checkedConvert('name', (v) => v as String),
        maxWorks: $checkedConvert('maxWorks', (v) => (v as num).toInt()),
      );
      return val;
    });

Map<String, dynamic> _$MeTierToJson(MeTier instance) => <String, dynamic>{
  'name': instance.name,
  'maxWorks': instance.maxWorks,
};
