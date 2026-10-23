//
// AUTO-GENERATED FILE, DO NOT MODIFY!
//

// ignore_for_file: unused_element
import 'package:json_annotation/json_annotation.dart';

part 'submit_dmca_notice_request.g.dart';


@JsonSerializable(
  checked: true,
  createToJson: true,
  disallowUnrecognizedKeys: false,
  explicitToJson: true,
)
class SubmitDmcaNoticeRequest {
  /// Returns a new [SubmitDmcaNoticeRequest] instance.
  SubmitDmcaNoticeRequest({

    required  this.claimantName,

    required  this.claimantEmail,

    required  this.claimantAddress,

     this.claimantPhone,

    required  this.workReference,

    required  this.originalWorkDescription,

    required  this.goodFaithStatement,

    required  this.perjuryStatement,

    required  this.signatureName,
  });

  @JsonKey(
    
    name: r'claimantName',
    required: true,
    includeIfNull: false,
  )


  final String claimantName;



  @JsonKey(
    
    name: r'claimantEmail',
    required: true,
    includeIfNull: false,
  )


  final String claimantEmail;



  @JsonKey(
    
    name: r'claimantAddress',
    required: true,
    includeIfNull: false,
  )


  final String claimantAddress;



  @JsonKey(
    
    name: r'claimantPhone',
    required: false,
    includeIfNull: false,
  )


  final String? claimantPhone;



      /// A story link ending in /s/{id}, or the bare story id
  @JsonKey(
    
    name: r'workReference',
    required: true,
    includeIfNull: false,
  )


  final String workReference;



  @JsonKey(
    
    name: r'originalWorkDescription',
    required: true,
    includeIfNull: false,
  )


  final String originalWorkDescription;



      /// Must be true
  @JsonKey(
    
    name: r'goodFaithStatement',
    required: true,
    includeIfNull: false,
  )


  final bool goodFaithStatement;



      /// Must be true
  @JsonKey(
    
    name: r'perjuryStatement',
    required: true,
    includeIfNull: false,
  )


  final bool perjuryStatement;



  @JsonKey(
    
    name: r'signatureName',
    required: true,
    includeIfNull: false,
  )


  final String signatureName;





    @override
    bool operator ==(Object other) => identical(this, other) || other is SubmitDmcaNoticeRequest &&
      other.claimantName == claimantName &&
      other.claimantEmail == claimantEmail &&
      other.claimantAddress == claimantAddress &&
      other.claimantPhone == claimantPhone &&
      other.workReference == workReference &&
      other.originalWorkDescription == originalWorkDescription &&
      other.goodFaithStatement == goodFaithStatement &&
      other.perjuryStatement == perjuryStatement &&
      other.signatureName == signatureName;

    @override
    int get hashCode =>
        claimantName.hashCode +
        claimantEmail.hashCode +
        claimantAddress.hashCode +
        (claimantPhone == null ? 0 : claimantPhone.hashCode) +
        workReference.hashCode +
        originalWorkDescription.hashCode +
        goodFaithStatement.hashCode +
        perjuryStatement.hashCode +
        signatureName.hashCode;

  factory SubmitDmcaNoticeRequest.fromJson(Map<String, dynamic> json) => _$SubmitDmcaNoticeRequestFromJson(json);

  Map<String, dynamic> toJson() => _$SubmitDmcaNoticeRequestToJson(this);

  @override
  String toString() {
    return toJson().toString();
  }

}

