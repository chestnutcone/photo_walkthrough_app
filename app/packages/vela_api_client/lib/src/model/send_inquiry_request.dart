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

    required  this.message,

     this.workId,
  });

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
      other.message == message &&
      other.workId == workId;

    @override
    int get hashCode =>
        message.hashCode +
        (workId == null ? 0 : workId.hashCode);

  factory SendInquiryRequest.fromJson(Map<String, dynamic> json) => _$SendInquiryRequestFromJson(json);

  Map<String, dynamic> toJson() => _$SendInquiryRequestToJson(this);

  @override
  String toString() {
    return toJson().toString();
  }

}

