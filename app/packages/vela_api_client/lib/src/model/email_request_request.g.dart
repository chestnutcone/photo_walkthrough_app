// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'email_request_request.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

EmailRequestRequest _$EmailRequestRequestFromJson(Map<String, dynamic> json) =>
    $checkedCreate('EmailRequestRequest', json, ($checkedConvert) {
      $checkKeys(json, requiredKeys: const ['email']);
      final val = EmailRequestRequest(
        email: $checkedConvert('email', (v) => v as String),
      );
      return val;
    });

Map<String, dynamic> _$EmailRequestRequestToJson(
  EmailRequestRequest instance,
) => <String, dynamic>{'email': instance.email};
