// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'sso_sign_in_request.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

SsoSignInRequest _$SsoSignInRequestFromJson(Map<String, dynamic> json) =>
    $checkedCreate('SsoSignInRequest', json, ($checkedConvert) {
      $checkKeys(json, requiredKeys: const ['provider', 'idToken', 'nonce']);
      final val = SsoSignInRequest(
        provider: $checkedConvert(
          'provider',
          (v) => $enumDecode(_$SsoSignInRequestProviderEnumEnumMap, v),
        ),
        idToken: $checkedConvert('idToken', (v) => v as String),
        nonce: $checkedConvert('nonce', (v) => v as String),
      );
      return val;
    });

Map<String, dynamic> _$SsoSignInRequestToJson(SsoSignInRequest instance) =>
    <String, dynamic>{
      'provider': _$SsoSignInRequestProviderEnumEnumMap[instance.provider]!,
      'idToken': instance.idToken,
      'nonce': instance.nonce,
    };

const _$SsoSignInRequestProviderEnumEnumMap = {
  SsoSignInRequestProviderEnum.apple: 'apple',
  SsoSignInRequestProviderEnum.google: 'google',
};
