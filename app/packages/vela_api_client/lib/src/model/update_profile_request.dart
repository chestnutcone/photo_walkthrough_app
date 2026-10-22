//
// AUTO-GENERATED FILE, DO NOT MODIFY!
//

// ignore_for_file: unused_element
import 'package:json_annotation/json_annotation.dart';

part 'update_profile_request.g.dart';


@JsonSerializable(
  checked: true,
  createToJson: true,
  disallowUnrecognizedKeys: false,
  explicitToJson: true,
)
class UpdateProfileRequest {
  /// Returns a new [UpdateProfileRequest] instance.
  UpdateProfileRequest({

     this.displayName,

     this.handle,

     this.bio,

     this.avatarUploaded,
  });

  @JsonKey(
    
    name: r'displayName',
    required: false,
    includeIfNull: false,
  )


  final String? displayName;



  @JsonKey(
    
    name: r'handle',
    required: false,
    includeIfNull: false,
  )


  final String? handle;



  @JsonKey(
    
    name: r'bio',
    required: false,
    includeIfNull: false,
  )


  final String? bio;



      /// Set true after uploading to the avatar upload URL
  @JsonKey(
    
    name: r'avatarUploaded',
    required: false,
    includeIfNull: false,
  )


  final bool? avatarUploaded;





    @override
    bool operator ==(Object other) => identical(this, other) || other is UpdateProfileRequest &&
      other.displayName == displayName &&
      other.handle == handle &&
      other.bio == bio &&
      other.avatarUploaded == avatarUploaded;

    @override
    int get hashCode =>
        displayName.hashCode +
        handle.hashCode +
        bio.hashCode +
        avatarUploaded.hashCode;

  factory UpdateProfileRequest.fromJson(Map<String, dynamic> json) => _$UpdateProfileRequestFromJson(json);

  Map<String, dynamic> toJson() => _$UpdateProfileRequestToJson(this);

  @override
  String toString() {
    return toJson().toString();
  }

}

