//
// AUTO-GENERATED FILE, DO NOT MODIFY!
//

// ignore_for_file: unused_element
import 'package:vela_api_client/src/model/chapter.dart';
import 'package:json_annotation/json_annotation.dart';

part 'update_work_request.g.dart';


@JsonSerializable(
  checked: true,
  createToJson: true,
  disallowUnrecognizedKeys: false,
  explicitToJson: true,
)
class UpdateWorkRequest {
  /// Returns a new [UpdateWorkRequest] instance.
  UpdateWorkRequest({

    required  this.title,

    required  this.chapters,
  });

  @JsonKey(
    
    name: r'title',
    required: true,
    includeIfNull: false,
  )


  final String title;



  @JsonKey(
    
    name: r'chapters',
    required: true,
    includeIfNull: false,
  )


  final List<Chapter> chapters;





    @override
    bool operator ==(Object other) => identical(this, other) || other is UpdateWorkRequest &&
      other.title == title &&
      other.chapters == chapters;

    @override
    int get hashCode =>
        title.hashCode +
        chapters.hashCode;

  factory UpdateWorkRequest.fromJson(Map<String, dynamic> json) => _$UpdateWorkRequestFromJson(json);

  Map<String, dynamic> toJson() => _$UpdateWorkRequestToJson(this);

  @override
  String toString() {
    return toJson().toString();
  }

}

