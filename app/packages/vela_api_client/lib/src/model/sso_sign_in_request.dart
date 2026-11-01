//
// AUTO-GENERATED FILE, DO NOT MODIFY!
//

// ignore_for_file: unused_element
import 'package:json_annotation/json_annotation.dart';

part 'sso_sign_in_request.g.dart';


@JsonSerializable(
  checked: true,
  createToJson: true,
  disallowUnrecognizedKeys: false,
  explicitToJson: true,
)
class SsoSignInRequest {
  /// Returns a new [SsoSignInRequest] instance.
  SsoSignInRequest({

    required  this.provider,

    required  this.idToken,

    required  this.nonce,
  });

  @JsonKey(
    
    name: r'provider',
    required: true,
    includeIfNull: false,
  )


  final SsoSignInRequestProviderEnum provider;



  @JsonKey(
    
    name: r'idToken',
    required: true,
    includeIfNull: false,
  )


  final String idToken;



  @JsonKey(
    
    name: r'nonce',
    required: true,
    includeIfNull: false,
  )


  final String nonce;





    @override
    bool operator ==(Object other) => identical(this, other) || other is SsoSignInRequest &&
      other.provider == provider &&
      other.idToken == idToken &&
      other.nonce == nonce;

    @override
    int get hashCode =>
        provider.hashCode +
        idToken.hashCode +
        nonce.hashCode;

  factory SsoSignInRequest.fromJson(Map<String, dynamic> json) => _$SsoSignInRequestFromJson(json);

  Map<String, dynamic> toJson() => _$SsoSignInRequestToJson(this);

  @override
  String toString() {
    return toJson().toString();
  }

}


enum SsoSignInRequestProviderEnum {
@JsonValue(r'apple')
apple(r'apple'),
@JsonValue(r'google')
google(r'google');

const SsoSignInRequestProviderEnum(this.value);

final String value;

@override
String toString() => value;
}


