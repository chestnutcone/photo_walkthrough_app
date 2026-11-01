//
// AUTO-GENERATED FILE, DO NOT MODIFY!
//

// ignore_for_file: unused_element
import 'package:vela_api_client/src/model/service_item.dart';
import 'package:vela_api_client/src/model/show_item.dart';
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
      other.city == city &&
      other.discipline == discipline &&
      other.availability == availability &&
      other.website == website &&
      other.social == social &&
      other.studio == studio &&
      other.replyTime == replyTime &&
      other.publicEmail == publicEmail &&
      other.services == services &&
      other.shows == shows &&
      other.avatarUploaded == avatarUploaded;

    @override
    int get hashCode =>
        displayName.hashCode +
        handle.hashCode +
        bio.hashCode +
        city.hashCode +
        discipline.hashCode +
        availability.hashCode +
        website.hashCode +
        social.hashCode +
        studio.hashCode +
        replyTime.hashCode +
        (publicEmail == null ? 0 : publicEmail.hashCode) +
        services.hashCode +
        shows.hashCode +
        avatarUploaded.hashCode;

  factory UpdateProfileRequest.fromJson(Map<String, dynamic> json) => _$UpdateProfileRequestFromJson(json);

  Map<String, dynamic> toJson() => _$UpdateProfileRequestToJson(this);

  @override
  String toString() {
    return toJson().toString();
  }

}

