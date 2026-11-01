// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'error_body.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

ErrorBody _$ErrorBodyFromJson(Map<String, dynamic> json) =>
    $checkedCreate('ErrorBody', json, ($checkedConvert) {
      $checkKeys(json, requiredKeys: const ['code', 'message']);
      final val = ErrorBody(
        code: $checkedConvert('code', (v) => v as String),
        message: $checkedConvert('message', (v) => v as String),
      );
      return val;
    });

Map<String, dynamic> _$ErrorBodyToJson(ErrorBody instance) => <String, dynamic>{
  'code': instance.code,
  'message': instance.message,
};
