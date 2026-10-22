// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'upload_ticket.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

UploadTicket _$UploadTicketFromJson(Map<String, dynamic> json) =>
    $checkedCreate('UploadTicket', json, ($checkedConvert) {
      $checkKeys(json, requiredKeys: const ['uploadUrl']);
      final val = UploadTicket(
        uploadUrl: $checkedConvert('uploadUrl', (v) => v as String),
      );
      return val;
    });

Map<String, dynamic> _$UploadTicketToJson(UploadTicket instance) =>
    <String, dynamic>{'uploadUrl': instance.uploadUrl};
