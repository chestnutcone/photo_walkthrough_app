//
// AUTO-GENERATED FILE, DO NOT MODIFY!
//

// ignore_for_file: unused_element
import 'package:vela_api_client/src/model/me_tier.dart';
import 'package:vela_api_client/src/model/profile.dart';
import 'package:json_annotation/json_annotation.dart';

part 'me.g.dart';


@JsonSerializable(
  checked: true,
  createToJson: true,
  disallowUnrecognizedKeys: false,
  explicitToJson: true,
)
class Me {
  /// Returns a new [Me] instance.
  Me({

    required  this.id,

    required  this.profile,

    required  this.onboardingComplete,

    required  this.tier,

    required  this.usedWorks,
  });

  @JsonKey(
    
    name: r'id',
    required: true,
    includeIfNull: false,
  )


  final String id;



  @JsonKey(
    
    name: r'profile',
    required: true,
    includeIfNull: true,
  )


  final Profile? profile;



  @JsonKey(
    
    name: r'onboardingComplete',
    required: true,
    includeIfNull: false,
  )


  final bool onboardingComplete;



  @JsonKey(
    
    name: r'tier',
    required: true,
    includeIfNull: false,
  )


  final MeTier tier;



      /// Works in draft, pending or published
  @JsonKey(
    
    name: r'usedWorks',
    required: true,
    includeIfNull: false,
  )


  final int usedWorks;





    @override
    bool operator ==(Object other) => identical(this, other) || other is Me &&
      other.id == id &&
      other.profile == profile &&
      other.onboardingComplete == onboardingComplete &&
      other.tier == tier &&
      other.usedWorks == usedWorks;

    @override
    int get hashCode =>
        id.hashCode +
        (profile == null ? 0 : profile.hashCode) +
        onboardingComplete.hashCode +
        tier.hashCode +
        usedWorks.hashCode;

  factory Me.fromJson(Map<String, dynamic> json) => _$MeFromJson(json);

  Map<String, dynamic> toJson() => _$MeToJson(this);

  @override
  String toString() {
    return toJson().toString();
  }

}

