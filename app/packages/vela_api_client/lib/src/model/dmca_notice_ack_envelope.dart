//
// AUTO-GENERATED FILE, DO NOT MODIFY!
//

// ignore_for_file: unused_element
import 'package:vela_api_client/src/model/error_body.dart';
import 'package:vela_api_client/src/model/dmca_notice_ack.dart';
import 'package:vela_api_client/src/model/page.dart';
import 'package:json_annotation/json_annotation.dart';

part 'dmca_notice_ack_envelope.g.dart';


@JsonSerializable(
  checked: true,
  createToJson: true,
  disallowUnrecognizedKeys: false,
  explicitToJson: true,
)
class DmcaNoticeAckEnvelope {
  /// Returns a new [DmcaNoticeAckEnvelope] instance.
  DmcaNoticeAckEnvelope({

    required  this.success,

    required  this.data,

    required  this.error,

    required  this.page,
  });

  @JsonKey(
    
    name: r'success',
    required: true,
    includeIfNull: false,
  )


  final bool success;



  @JsonKey(
    
    name: r'data',
    required: true,
    includeIfNull: true,
  )


  final DmcaNoticeAck? data;



  @JsonKey(
    
    name: r'error',
    required: true,
    includeIfNull: true,
  )


  final ErrorBody? error;



  @JsonKey(
    
    name: r'page',
    required: true,
    includeIfNull: true,
  )


  final Page? page;





    @override
    bool operator ==(Object other) => identical(this, other) || other is DmcaNoticeAckEnvelope &&
      other.success == success &&
      other.data == data &&
      other.error == error &&
      other.page == page;

    @override
    int get hashCode =>
        success.hashCode +
        (data == null ? 0 : data.hashCode) +
        (error == null ? 0 : error.hashCode) +
        (page == null ? 0 : page.hashCode);

  factory DmcaNoticeAckEnvelope.fromJson(Map<String, dynamic> json) => _$DmcaNoticeAckEnvelopeFromJson(json);

  Map<String, dynamic> toJson() => _$DmcaNoticeAckEnvelopeToJson(this);

  @override
  String toString() {
    return toJson().toString();
  }

}

