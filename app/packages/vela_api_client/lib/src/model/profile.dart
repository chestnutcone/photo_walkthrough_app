//
// AUTO-GENERATED FILE, DO NOT MODIFY!
//

// ignore_for_file: unused_element
import 'package:vela_api_client/src/model/service_item.dart';
import 'package:vela_api_client/src/model/show_item.dart';
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

     this.chapterCount,

     this.isFollowing,

    required  this.onboardingComplete,

     this.city,

     this.discipline,

     this.availability,

     this.website,

     this.social,

     this.studio,

     this.replyTime,

     this.publicEmail,

     this.services,

     this.shows,
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



      /// Published stories
  @JsonKey(
    
    name: r'workCount',
    required: true,
    includeIfNull: false,
  )


  final int workCount;



      /// Chapters of published stories
  @JsonKey(
    
    name: r'chapterCount',
    required: false,
    includeIfNull: false,
  )


  final int? chapterCount;



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



  @JsonKey(
    
    name: r'city',
    required: false,
    includeIfNull: false,
  )


  final String? city;



  @JsonKey(
    
    name: r'discipline',
    required: false,
    includeIfNull: false,
  )


  final String? discipline;



  @JsonKey(
    
    name: r'availability',
    required: false,
    includeIfNull: false,
  )


  final String? availability;



  @JsonKey(
    
    name: r'website',
    required: false,
    includeIfNull: false,
  )


  final String? website;



  @JsonKey(
    
    name: r'social',
    required: false,
    includeIfNull: false,
  )


  final String? social;



  @JsonKey(
    
    name: r'studio',
    required: false,
    includeIfNull: false,
  )


  final String? studio;



  @JsonKey(
    
    name: r'replyTime',
    required: false,
    includeIfNull: false,
  )


  final String? replyTime;



      /// Optional public contact email; the only email ever exposed
  @JsonKey(
    
    name: r'publicEmail',
    required: false,
    includeIfNull: false,
  )


  final String? publicEmail;



  @JsonKey(
    
    name: r'services',
    required: false,
    includeIfNull: false,
  )


  final List<ServiceItem>? services;



  @JsonKey(
    
    name: r'shows',
    required: false,
    includeIfNull: false,
  )


  final List<ShowItem>? shows;





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
      other.chapterCount == chapterCount &&
      other.isFollowing == isFollowing &&
      other.onboardingComplete == onboardingComplete &&
      other.city == city &&
      other.discipline == discipline &&
      other.availability == availability &&
      other.website == website &&
      other.social == social &&
      other.studio == studio &&
      other.replyTime == replyTime &&
      other.publicEmail == publicEmail &&
      other.services == services &&
      other.shows == shows;

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
        chapterCount.hashCode +
        (isFollowing == null ? 0 : isFollowing.hashCode) +
        onboardingComplete.hashCode +
        city.hashCode +
        discipline.hashCode +
        availability.hashCode +
        website.hashCode +
        social.hashCode +
        studio.hashCode +
        replyTime.hashCode +
        (publicEmail == null ? 0 : publicEmail.hashCode) +
        services.hashCode +
        shows.hashCode;

  factory Profile.fromJson(Map<String, dynamic> json) => _$ProfileFromJson(json);

  Map<String, dynamic> toJson() => _$ProfileToJson(this);

  @override
  String toString() {
    return toJson().toString();
  }

}

