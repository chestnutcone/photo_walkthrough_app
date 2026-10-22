//
// AUTO-GENERATED FILE, DO NOT MODIFY!
//

// ignore_for_file: unused_element
import 'package:json_annotation/json_annotation.dart';

part 'report_request.g.dart';


@JsonSerializable(
  checked: true,
  createToJson: true,
  disallowUnrecognizedKeys: false,
  explicitToJson: true,
)
class ReportRequest {
  /// Returns a new [ReportRequest] instance.
  ReportRequest({

    required  this.targetType,

    required  this.targetId,

    required  this.reason,
  });

  @JsonKey(
    
    name: r'targetType',
    required: true,
    includeIfNull: false,
  )


  final ReportRequestTargetTypeEnum targetType;



  @JsonKey(
    
    name: r'targetId',
    required: true,
    includeIfNull: false,
  )


  final String targetId;



  @JsonKey(
    
    name: r'reason',
    required: true,
    includeIfNull: false,
  )


  final String reason;





    @override
    bool operator ==(Object other) => identical(this, other) || other is ReportRequest &&
      other.targetType == targetType &&
      other.targetId == targetId &&
      other.reason == reason;

    @override
    int get hashCode =>
        targetType.hashCode +
        targetId.hashCode +
        reason.hashCode;

  factory ReportRequest.fromJson(Map<String, dynamic> json) => _$ReportRequestFromJson(json);

  Map<String, dynamic> toJson() => _$ReportRequestToJson(this);

  @override
  String toString() {
    return toJson().toString();
  }

}


enum ReportRequestTargetTypeEnum {
@JsonValue(r'work')
work(r'work'),
@JsonValue(r'reply')
reply(r'reply');

const ReportRequestTargetTypeEnum(this.value);

final String value;

@override
String toString() => value;
}


