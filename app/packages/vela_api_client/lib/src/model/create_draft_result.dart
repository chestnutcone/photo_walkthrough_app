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

    required  this.expiresAt,
  });

  @JsonKey(
    
    name: r'workId',
    required: true,
    includeIfNull: false,
  )


  final String workId;



      /// Write-only signed URL for the original image (PUT with x-ms-blob-type: BlockBlob), valid for at most 15 minutes
  @JsonKey(
    
    name: r'uploadUrl',
    required: true,
    includeIfNull: false,
  )


  final String uploadUrl;



      /// When the upload URL stops working (the draft itself lives 24 hours)
  @JsonKey(
    
    name: r'expiresAt',
    required: true,
    includeIfNull: false,
  )


  final DateTime expiresAt;





    @override
    bool operator ==(Object other) => identical(this, other) || other is CreateDraftResult &&
      other.workId == workId &&
      other.uploadUrl == uploadUrl &&
      other.expiresAt == expiresAt;

    @override
    int get hashCode =>
        workId.hashCode +
        uploadUrl.hashCode +
        expiresAt.hashCode;

  factory CreateDraftResult.fromJson(Map<String, dynamic> json) => _$CreateDraftResultFromJson(json);

  Map<String, dynamic> toJson() => _$CreateDraftResultToJson(this);

  @override
  String toString() {
    return toJson().toString();
  }

}

