//
// AUTO-GENERATED FILE, DO NOT MODIFY!
//

// ignore_for_file: unused_element
import 'package:json_annotation/json_annotation.dart';

part 'create_draft_result.g.dart';


@JsonSerializable(
  checked: true,
  createToJson: true,
  disallowUnrecognizedKeys: false,
  explicitToJson: true,
)
class CreateDraftResult {
  /// Returns a new [CreateDraftResult] instance.
  CreateDraftResult({

    required  this.workId,

    required  this.uploadUrl,
  });

  @JsonKey(
    
    name: r'workId',
    required: true,
    includeIfNull: false,
  )


  final String workId;



  @JsonKey(
    
    name: r'uploadUrl',
    required: true,
    includeIfNull: false,
  )


  final String uploadUrl;





    @override
    bool operator ==(Object other) => identical(this, other) || other is CreateDraftResult &&
      other.workId == workId &&
      other.uploadUrl == uploadUrl;

    @override
    int get hashCode =>
        workId.hashCode +
        uploadUrl.hashCode;

  factory CreateDraftResult.fromJson(Map<String, dynamic> json) => _$CreateDraftResultFromJson(json);

  Map<String, dynamic> toJson() => _$CreateDraftResultToJson(this);

  @override
  String toString() {
    return toJson().toString();
  }

}

