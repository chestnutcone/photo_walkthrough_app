//
// AUTO-GENERATED FILE, DO NOT MODIFY!
//

// ignore_for_file: unused_element
import 'package:json_annotation/json_annotation.dart';

part 'add_reply_request.g.dart';


@JsonSerializable(
  checked: true,
  createToJson: true,
  disallowUnrecognizedKeys: false,
  explicitToJson: true,
)
class AddReplyRequest {
  /// Returns a new [AddReplyRequest] instance.
  AddReplyRequest({

    required  this.body,
  });

  @JsonKey(
    
    name: r'body',
    required: true,
    includeIfNull: false,
  )


  final String body;





    @override
    bool operator ==(Object other) => identical(this, other) || other is AddReplyRequest &&
      other.body == body;

    @override
    int get hashCode =>
        body.hashCode;

  factory AddReplyRequest.fromJson(Map<String, dynamic> json) => _$AddReplyRequestFromJson(json);

  Map<String, dynamic> toJson() => _$AddReplyRequestToJson(this);

  @override
  String toString() {
    return toJson().toString();
  }

}

