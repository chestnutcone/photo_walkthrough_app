//
// AUTO-GENERATED FILE, DO NOT MODIFY!
//

// ignore_for_file: unused_element
import 'package:json_annotation/json_annotation.dart';

part 'token_pair.g.dart';


@JsonSerializable(
  checked: true,
  createToJson: true,
  disallowUnrecognizedKeys: false,
  explicitToJson: true,
)
class TokenPair {
  /// Returns a new [TokenPair] instance.
  TokenPair({

    required  this.accessToken,

    required  this.refreshToken,

    required  this.expiresIn,

    required  this.isNewUser,
  });

  @JsonKey(
    
    name: r'accessToken',
    required: true,
    includeIfNull: false,
  )


  final String accessToken;



  @JsonKey(
    
    name: r'refreshToken',
    required: true,
    includeIfNull: false,
  )


  final String refreshToken;



      /// Access token lifetime in seconds
  @JsonKey(
    
    name: r'expiresIn',
    required: true,
    includeIfNull: false,
  )


  final int expiresIn;



      /// True until onboarding is completed
  @JsonKey(
    
    name: r'isNewUser',
    required: true,
    includeIfNull: false,
  )


  final bool isNewUser;





    @override
    bool operator ==(Object other) => identical(this, other) || other is TokenPair &&
      other.accessToken == accessToken &&
      other.refreshToken == refreshToken &&
      other.expiresIn == expiresIn &&
      other.isNewUser == isNewUser;

    @override
    int get hashCode =>
        accessToken.hashCode +
        refreshToken.hashCode +
        expiresIn.hashCode +
        isNewUser.hashCode;

  factory TokenPair.fromJson(Map<String, dynamic> json) => _$TokenPairFromJson(json);

  Map<String, dynamic> toJson() => _$TokenPairToJson(this);

  @override
  String toString() {
    return toJson().toString();
  }

}

