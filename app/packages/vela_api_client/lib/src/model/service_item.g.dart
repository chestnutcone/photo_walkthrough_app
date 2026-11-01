// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'service_item.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

ServiceItem _$ServiceItemFromJson(Map<String, dynamic> json) =>
    $checkedCreate('ServiceItem', json, ($checkedConvert) {
      $checkKeys(json, requiredKeys: const ['title', 'description', 'price']);
      final val = ServiceItem(
        title: $checkedConvert('title', (v) => v as String),
        description: $checkedConvert('description', (v) => v as String),
        price: $checkedConvert('price', (v) => v as String),
      );
      return val;
    });

Map<String, dynamic> _$ServiceItemToJson(ServiceItem instance) =>
    <String, dynamic>{
      'title': instance.title,
      'description': instance.description,
      'price': instance.price,
    };
