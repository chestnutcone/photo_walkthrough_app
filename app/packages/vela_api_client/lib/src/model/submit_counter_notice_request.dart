//
// AUTO-GENERATED FILE, DO NOT MODIFY!
//

// ignore_for_file: unused_element
import 'package:json_annotation/json_annotation.dart';

part 'submit_counter_notice_request.g.dart';


@JsonSerializable(
  checked: true,
  createToJson: true,
  disallowUnrecognizedKeys: false,
  explicitToJson: true,
)
class SubmitCounterNoticeRequest {
  /// Returns a new [SubmitCounterNoticeRequest] instance.
  SubmitCounterNoticeRequest({

    required  this.name,

    required  this.address,

     this.phone,

    required  this.statement,

    required  this.consentToJurisdiction,

    required  this.goodFaithBelief,
  });

  @JsonKey(
    
    name: r'name',
    required: true,
    includeIfNull: false,
  )


  final String name;



  @JsonKey(
    
    name: r'address',
    required: true,
    includeIfNull: false,
  )


  final String address;



  @JsonKey(
    
    name: r'phone',
    required: false,
    includeIfNull: false,
  )


  final String? phone;



  @JsonKey(
    
    name: r'statement',
    required: true,
    includeIfNull: false,
  )


  final String statement;



      /// Must be true
  @JsonKey(
    
    name: r'consentToJurisdiction',
    required: true,
    includeIfNull: false,
  )


  final bool consentToJurisdiction;



      /// Must be true
  @JsonKey(
    
    name: r'goodFaithBelief',
    required: true,
    includeIfNull: false,
  )


  final bool goodFaithBelief;





    @override
    bool operator ==(Object other) => identical(this, other) || other is SubmitCounterNoticeRequest &&
      other.name == name &&
      other.address == address &&
      other.phone == phone &&
      other.statement == statement &&
      other.consentToJurisdiction == consentToJurisdiction &&
      other.goodFaithBelief == goodFaithBelief;

    @override
    int get hashCode =>
        name.hashCode +
        address.hashCode +
        (phone == null ? 0 : phone.hashCode) +
        statement.hashCode +
        consentToJurisdiction.hashCode +
        goodFaithBelief.hashCode;

  factory SubmitCounterNoticeRequest.fromJson(Map<String, dynamic> json) => _$SubmitCounterNoticeRequestFromJson(json);

  Map<String, dynamic> toJson() => _$SubmitCounterNoticeRequestToJson(this);

  @override
  String toString() {
    return toJson().toString();
  }

}

