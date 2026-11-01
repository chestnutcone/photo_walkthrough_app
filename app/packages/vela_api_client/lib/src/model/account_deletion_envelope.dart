//
// AUTO-GENERATED FILE, DO NOT MODIFY!
//

// ignore_for_file: unused_element
import 'package:vela_api_client/src/model/error_body.dart';
import 'package:vela_api_client/src/model/account_deletion.dart';
import 'package:vela_api_client/src/model/page.dart';
import 'package:json_annotation/json_annotation.dart';

part 'account_deletion_envelope.g.dart';


@JsonSerializable(
  checked: true,
  createToJson: true,
  disallowUnrecognizedKeys: false,
  explicitToJson: true,
)
class AccountDeletionEnvelope {
  /// Returns a new [AccountDeletionEnvelope] instance.
  AccountDeletionEnvelope({

    required  this.success,

    required  this.data,

    required  this.error,

    required  this.page,
  });

  @JsonKey(
    
    name: r'success',
    required: true,
    includeIfNull: false,
  )


  final bool success;



  @JsonKey(
    
    name: r'data',
    required: true,
    includeIfNull: true,
  )


  final AccountDeletion? data;



  @JsonKey(
    
    name: r'error',
    required: true,
    includeIfNull: true,
  )


  final ErrorBody? error;



  @JsonKey(
    
    name: r'page',
    required: true,
    includeIfNull: true,
  )


  final Page? page;





    @override
    bool operator ==(Object other) => identical(this, other) || other is AccountDeletionEnvelope &&
      other.success == success &&
      other.data == data &&
      other.error == error &&
      other.page == page;

    @override
    int get hashCode =>
        success.hashCode +
        (data == null ? 0 : data.hashCode) +
        (error == null ? 0 : error.hashCode) +
        (page == null ? 0 : page.hashCode);

  factory AccountDeletionEnvelope.fromJson(Map<String, dynamic> json) => _$AccountDeletionEnvelopeFromJson(json);

  Map<String, dynamic> toJson() => _$AccountDeletionEnvelopeToJson(this);

  @override
  String toString() {
    return toJson().toString();
  }

}

