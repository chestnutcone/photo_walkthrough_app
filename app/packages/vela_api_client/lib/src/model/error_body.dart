//
// AUTO-GENERATED FILE, DO NOT MODIFY!
//

// ignore_for_file: unused_element
import 'package:json_annotation/json_annotation.dart';

part 'error_body.g.dart';


@JsonSerializable(
  checked: true,
  createToJson: true,
  disallowUnrecognizedKeys: false,
  explicitToJson: true,
)
class ErrorBody {
  /// Returns a new [ErrorBody] instance.
  ErrorBody({

    required  this.code,

    required  this.message,
  });

  @JsonKey(
    
    name: r'code',
    required: true,
    includeIfNull: false,
  )


  final String code;



  @JsonKey(
    
    name: r'message',
    required: true,
    includeIfNull: false,
  )


  final String message;





    @override
    bool operator ==(Object other) => identical(this, other) || other is ErrorBody &&
      other.code == code &&
      other.message == message;

    @override
    int get hashCode =>
        code.hashCode +
        message.hashCode;

  factory ErrorBody.fromJson(Map<String, dynamic> json) => _$ErrorBodyFromJson(json);

  Map<String, dynamic> toJson() => _$ErrorBodyToJson(this);

  @override
  String toString() {
    return toJson().toString();
  }

}

