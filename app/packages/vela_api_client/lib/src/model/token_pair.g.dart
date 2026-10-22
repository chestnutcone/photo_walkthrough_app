// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'token_pair.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

TokenPair _$TokenPairFromJson(Map<String, dynamic> json) =>
    $checkedCreate('TokenPair', json, ($checkedConvert) {
      $checkKeys(
        json,
        requiredKeys: const [
          'accessToken',
          'refreshToken',
          'expiresIn',
          'isNewUser',
        ],
      );
      final val = TokenPair(
        accessToken: $checkedConvert('accessToken', (v) => v as String),
        refreshToken: $checkedConvert('refreshToken', (v) => v as String),
        expiresIn: $checkedConvert('expiresIn', (v) => (v as num).toInt()),
        isNewUser: $checkedConvert('isNewUser', (v) => v as bool),
      );
      return val;
    });

Map<String, dynamic> _$TokenPairToJson(TokenPair instance) => <String, dynamic>{
  'accessToken': instance.accessToken,
  'refreshToken': instance.refreshToken,
  'expiresIn': instance.expiresIn,
  'isNewUser': instance.isNewUser,
};
