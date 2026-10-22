//
// AUTO-GENERATED FILE, DO NOT MODIFY!
//

// ignore_for_file: unused_element
import 'package:json_annotation/json_annotation.dart';

part 'send_inquiry_request.g.dart';


@JsonSerializable(
  checked: true,
  createToJson: true,
  disallowUnrecognizedKeys: false,
  explicitToJson: true,
)
class SendInquiryRequest {
  /// Returns a new [SendInquiryRequest] instance.
  SendInquiryRequest({

    required  this.type,

    required  this.email,

    required  this.message,

     this.workId,
  });

  @JsonKey(
    
    name: r'type',
    required: true,
    includeIfNull: false,
  )


  final SendInquiryRequestTypeEnum type;



      /// Sender's reply email
  @JsonKey(
    
    name: r'email',
    required: true,
    includeIfNull: false,
  )


  final String email;



      /// Trimmed; no control characters other than new lines
  @JsonKey(
    
    name: r'message',
    required: true,
    includeIfNull: false,
  )


  final String message;



  @JsonKey(
    
    name: r'workId',
    required: false,
    includeIfNull: false,
  )


  final String? workId;





    @override
    bool operator ==(Object other) => identical(this, other) || other is SendInquiryRequest &&
      other.type == type &&
      other.email == email &&
      other.message == message &&
      other.workId == workId;

    @override
    int get hashCode =>
        type.hashCode +
        email.hashCode +
        message.hashCode +
        (workId == null ? 0 : workId.hashCode);

  factory SendInquiryRequest.fromJson(Map<String, dynamic> json) => _$SendInquiryRequestFromJson(json);

  Map<String, dynamic> toJson() => _$SendInquiryRequestToJson(this);

  @override
  String toString() {
    return toJson().toString();
  }

}


enum SendInquiryRequestTypeEnum {
@JsonValue(r'Commission')
commission(r'Commission'),
@JsonValue(r'Buy a print')
buyAPrint(r'Buy a print'),
@JsonValue(r'Licensing')
licensing(r'Licensing'),
@JsonValue(r'Collaboration')
collaboration(r'Collaboration'),
@JsonValue(r'Just saying hi')
justSayingHi(r'Just saying hi');

const SendInquiryRequestTypeEnum(this.value);

final String value;

@override
String toString() => value;
}


