//
// AUTO-GENERATED FILE, DO NOT MODIFY!
//

// ignore_for_file: unused_element
import 'package:json_annotation/json_annotation.dart';

part 'upload_ticket.g.dart';


@JsonSerializable(
  checked: true,
  createToJson: true,
  disallowUnrecognizedKeys: false,
  explicitToJson: true,
)
class UploadTicket {
  /// Returns a new [UploadTicket] instance.
  UploadTicket({

    required  this.uploadUrl,

    required  this.expiresAt,
  });

      /// Write-only signed URL for one blob (PUT with x-ms-blob-type: BlockBlob), valid for at most 15 minutes
  @JsonKey(
    
    name: r'uploadUrl',
    required: true,
    includeIfNull: false,
  )


  final String uploadUrl;



      /// When the upload URL stops working
  @JsonKey(
    
    name: r'expiresAt',
    required: true,
    includeIfNull: false,
  )


  final DateTime expiresAt;





    @override
    bool operator ==(Object other) => identical(this, other) || other is UploadTicket &&
      other.uploadUrl == uploadUrl &&
      other.expiresAt == expiresAt;

    @override
    int get hashCode =>
        uploadUrl.hashCode +
        expiresAt.hashCode;

  factory UploadTicket.fromJson(Map<String, dynamic> json) => _$UploadTicketFromJson(json);

  Map<String, dynamic> toJson() => _$UploadTicketToJson(this);

  @override
  String toString() {
    return toJson().toString();
  }

}

