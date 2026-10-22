// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'add_reply_request.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

AddReplyRequest _$AddReplyRequestFromJson(Map<String, dynamic> json) =>
    $checkedCreate('AddReplyRequest', json, ($checkedConvert) {
      $checkKeys(json, requiredKeys: const ['body']);
      final val = AddReplyRequest(
        body: $checkedConvert('body', (v) => v as String),
      );
      return val;
    });

Map<String, dynamic> _$AddReplyRequestToJson(AddReplyRequest instance) =>
    <String, dynamic>{'body': instance.body};
