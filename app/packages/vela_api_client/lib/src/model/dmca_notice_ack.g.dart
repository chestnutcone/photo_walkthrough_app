// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'dmca_notice_ack.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

DmcaNoticeAck _$DmcaNoticeAckFromJson(Map<String, dynamic> json) =>
    $checkedCreate('DmcaNoticeAck', json, ($checkedConvert) {
      $checkKeys(json, requiredKeys: const ['noticeId']);
      final val = DmcaNoticeAck(
        noticeId: $checkedConvert('noticeId', (v) => v as String),
      );
      return val;
    });

Map<String, dynamic> _$DmcaNoticeAckToJson(DmcaNoticeAck instance) =>
    <String, dynamic>{'noticeId': instance.noticeId};
