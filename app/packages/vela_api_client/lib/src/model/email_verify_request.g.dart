// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'email_verify_request.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

EmailVerifyRequest _$EmailVerifyRequestFromJson(Map<String, dynamic> json) =>
    $checkedCreate('EmailVerifyRequest', json, ($checkedConvert) {
      $checkKeys(json, requiredKeys: const ['email', 'code']);
      final val = EmailVerifyRequest(
        email: $checkedConvert('email', (v) => v as String),
        code: $checkedConvert('code', (v) => v as String),
      );
      return val;
    });

Map<String, dynamic> _$EmailVerifyRequestToJson(EmailVerifyRequest instance) =>
    <String, dynamic>{'email': instance.email, 'code': instance.code};
