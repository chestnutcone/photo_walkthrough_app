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

     this.note,
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


  final ReportRequestReasonEnum reason;



  @JsonKey(
    
    name: r'note',
    required: false,
    includeIfNull: false,
  )


  final String? note;





    @override
    bool operator ==(Object other) => identical(this, other) || other is ReportRequest &&
      other.targetType == targetType &&
      other.targetId == targetId &&
      other.reason == reason &&
      other.note == note;

    @override
    int get hashCode =>
        targetType.hashCode +
        targetId.hashCode +
        reason.hashCode +
        note.hashCode;

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
reply(r'reply'),
@JsonValue(r'user')
user(r'user');

const ReportRequestTargetTypeEnum(this.value);

final String value;

@override
String toString() => value;
}



enum ReportRequestReasonEnum {
@JsonValue(r'illegal_content')
illegalContent(r'illegal_content'),
@JsonValue(r'copyright')
copyright(r'copyright'),
@JsonValue(r'harassment')
harassment(r'harassment'),
@JsonValue(r'spam')
spam(r'spam'),
@JsonValue(r'other')
other(r'other');

const ReportRequestReasonEnum(this.value);

final String value;

@override
String toString() => value;
}


