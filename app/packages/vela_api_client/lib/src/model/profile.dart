//
// AUTO-GENERATED FILE, DO NOT MODIFY!
//

// ignore_for_file: unused_element
import 'package:json_annotation/json_annotation.dart';

part 'profile.g.dart';


@JsonSerializable(
  checked: true,
  createToJson: true,
  disallowUnrecognizedKeys: false,
  explicitToJson: true,
)
class Profile {
  /// Returns a new [Profile] instance.
  Profile({

    required  this.id,

    required  this.handle,

    required  this.displayName,

    required  this.bio,

     this.avatarUrl,

    required  this.followerCount,

    required  this.followingCount,

    required  this.workCount,

     this.isFollowing,

    required  this.onboardingComplete,
  });

  @JsonKey(
    
    name: r'id',
    required: true,
    includeIfNull: false,
  )


  final String id;



  @JsonKey(
    
    name: r'handle',
    required: true,
    includeIfNull: false,
  )


  final String handle;



  @JsonKey(
    
    name: r'displayName',
    required: true,
    includeIfNull: false,
  )


  final String displayName;



  @JsonKey(
    
    name: r'bio',
    required: true,
    includeIfNull: false,
  )


  final String bio;



  @JsonKey(
    
    name: r'avatarUrl',
    required: false,
    includeIfNull: false,
  )


  final String? avatarUrl;



  @JsonKey(
    
    name: r'followerCount',
    required: true,
    includeIfNull: false,
  )


  final int followerCount;



  @JsonKey(
    
    name: r'followingCount',
    required: true,
    includeIfNull: false,
  )


  final int followingCount;



  @JsonKey(
    
    name: r'workCount',
    required: true,
    includeIfNull: false,
  )


  final int workCount;



      /// Null when anonymous or viewing self
  @JsonKey(
    
    name: r'isFollowing',
    required: false,
    includeIfNull: false,
  )


  final bool? isFollowing;



  @JsonKey(
    
    name: r'onboardingComplete',
    required: true,
    includeIfNull: false,
  )


  final bool onboardingComplete;





    @override
    bool operator ==(Object other) => identical(this, other) || other is Profile &&
      other.id == id &&
      other.handle == handle &&
      other.displayName == displayName &&
      other.bio == bio &&
      other.avatarUrl == avatarUrl &&
      other.followerCount == followerCount &&
      other.followingCount == followingCount &&
      other.workCount == workCount &&
      other.isFollowing == isFollowing &&
      other.onboardingComplete == onboardingComplete;

    @override
    int get hashCode =>
        id.hashCode +
        handle.hashCode +
        displayName.hashCode +
        bio.hashCode +
        (avatarUrl == null ? 0 : avatarUrl.hashCode) +
        followerCount.hashCode +
        followingCount.hashCode +
        workCount.hashCode +
        (isFollowing == null ? 0 : isFollowing.hashCode) +
        onboardingComplete.hashCode;

  factory Profile.fromJson(Map<String, dynamic> json) => _$ProfileFromJson(json);

  Map<String, dynamic> toJson() => _$ProfileToJson(this);

  @override
  String toString() {
    return toJson().toString();
  }

}

