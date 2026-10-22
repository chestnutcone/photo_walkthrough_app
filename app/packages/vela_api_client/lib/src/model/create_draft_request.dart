//
// AUTO-GENERATED FILE, DO NOT MODIFY!
//

// ignore_for_file: unused_element
import 'package:json_annotation/json_annotation.dart';

part 'create_draft_request.g.dart';


@JsonSerializable(
  checked: true,
  createToJson: true,
  disallowUnrecognizedKeys: false,
  explicitToJson: true,
)
class CreateDraftRequest {
  /// Returns a new [CreateDraftRequest] instance.
  CreateDraftRequest({

     this.title,

    required  this.contentType,
  });

  @JsonKey(
    
    name: r'title',
    required: false,
    includeIfNull: false,
  )


  final String? title;



  @JsonKey(
    
    name: r'contentType',
    required: true,
    includeIfNull: false,
  )


  final CreateDraftRequestContentTypeEnum contentType;





    @override
    bool operator ==(Object other) => identical(this, other) || other is CreateDraftRequest &&
      other.title == title &&
      other.contentType == contentType;

    @override
    int get hashCode =>
        (title == null ? 0 : title.hashCode) +
        contentType.hashCode;

  factory CreateDraftRequest.fromJson(Map<String, dynamic> json) => _$CreateDraftRequestFromJson(json);

  Map<String, dynamic> toJson() => _$CreateDraftRequestToJson(this);

  @override
  String toString() {
    return toJson().toString();
  }

}


enum CreateDraftRequestContentTypeEnum {
@JsonValue(r'image/jpeg')
imageSlashJpeg(r'image/jpeg'),
@JsonValue(r'image/png')
imageSlashPng(r'image/png'),
@JsonValue(r'image/webp')
imageSlashWebp(r'image/webp');

const CreateDraftRequestContentTypeEnum(this.value);

final String value;

@override
String toString() => value;
}


