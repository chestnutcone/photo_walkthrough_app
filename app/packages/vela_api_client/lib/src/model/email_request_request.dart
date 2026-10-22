//
// AUTO-GENERATED FILE, DO NOT MODIFY!
//

// ignore_for_file: unused_element
import 'package:json_annotation/json_annotation.dart';

part 'email_request_request.g.dart';


@JsonSerializable(
  checked: true,
  createToJson: true,
  disallowUnrecognizedKeys: false,
  explicitToJson: true,
)
class EmailRequestRequest {
  /// Returns a new [EmailRequestRequest] instance.
  EmailRequestRequest({

    required  this.email,
  });

  @JsonKey(
    
    name: r'email',
    required: true,
    includeIfNull: false,
  )


  final String email;





    @override
    bool operator ==(Object other) => identical(this, other) || other is EmailRequestRequest &&
      other.email == email;

    @override
    int get hashCode =>
        email.hashCode;

  factory EmailRequestRequest.fromJson(Map<String, dynamic> json) => _$EmailRequestRequestFromJson(json);

  Map<String, dynamic> toJson() => _$EmailRequestRequestToJson(this);

  @override
  String toString() {
    return toJson().toString();
  }

}

