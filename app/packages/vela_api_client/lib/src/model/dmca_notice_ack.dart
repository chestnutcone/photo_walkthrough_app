//
// AUTO-GENERATED FILE, DO NOT MODIFY!
//

// ignore_for_file: unused_element
import 'package:json_annotation/json_annotation.dart';

part 'dmca_notice_ack.g.dart';


@JsonSerializable(
  checked: true,
  createToJson: true,
  disallowUnrecognizedKeys: false,
  explicitToJson: true,
)
class DmcaNoticeAck {
  /// Returns a new [DmcaNoticeAck] instance.
  DmcaNoticeAck({

    required  this.noticeId,
  });

  @JsonKey(
    
    name: r'noticeId',
    required: true,
    includeIfNull: false,
  )


  final String noticeId;





    @override
    bool operator ==(Object other) => identical(this, other) || other is DmcaNoticeAck &&
      other.noticeId == noticeId;

    @override
    int get hashCode =>
        noticeId.hashCode;

  factory DmcaNoticeAck.fromJson(Map<String, dynamic> json) => _$DmcaNoticeAckFromJson(json);

  Map<String, dynamic> toJson() => _$DmcaNoticeAckToJson(this);

  @override
  String toString() {
    return toJson().toString();
  }

}

