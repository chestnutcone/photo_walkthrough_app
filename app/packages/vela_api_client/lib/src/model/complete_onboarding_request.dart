//
// AUTO-GENERATED FILE, DO NOT MODIFY!
//

// ignore_for_file: unused_element
import 'package:json_annotation/json_annotation.dart';

part 'complete_onboarding_request.g.dart';


@JsonSerializable(
  checked: true,
  createToJson: true,
  disallowUnrecognizedKeys: false,
  explicitToJson: true,
)
class CompleteOnboardingRequest {
  /// Returns a new [CompleteOnboardingRequest] instance.
  CompleteOnboardingRequest({

    required  this.displayName,

    required  this.handle,

    required  this.ageConfirmed,

    required  this.termsAccepted,

    required  this.followArtistIds,
  });

  @JsonKey(
    
    name: r'displayName',
    required: true,
    includeIfNull: false,
  )


  final String displayName;



  @JsonKey(
    
    name: r'handle',
    required: true,
    includeIfNull: false,
  )


  final String handle;



  @JsonKey(
    
    name: r'ageConfirmed',
    required: true,
    includeIfNull: false,
  )


  final bool ageConfirmed;



  @JsonKey(
    
    name: r'termsAccepted',
    required: true,
    includeIfNull: false,
  )


  final bool termsAccepted;



  @JsonKey(
    
    name: r'followArtistIds',
    required: true,
    includeIfNull: false,
  )


  final List<String> followArtistIds;





    @override
    bool operator ==(Object other) => identical(this, other) || other is CompleteOnboardingRequest &&
      other.displayName == displayName &&
      other.handle == handle &&
      other.ageConfirmed == ageConfirmed &&
      other.termsAccepted == termsAccepted &&
      other.followArtistIds == followArtistIds;

    @override
    int get hashCode =>
        displayName.hashCode +
        handle.hashCode +
        ageConfirmed.hashCode +
        termsAccepted.hashCode +
        followArtistIds.hashCode;

  factory CompleteOnboardingRequest.fromJson(Map<String, dynamic> json) => _$CompleteOnboardingRequestFromJson(json);

  Map<String, dynamic> toJson() => _$CompleteOnboardingRequestToJson(this);

  @override
  String toString() {
    return toJson().toString();
  }

}

