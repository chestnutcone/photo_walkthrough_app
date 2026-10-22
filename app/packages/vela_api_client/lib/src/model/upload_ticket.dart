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
  });

      /// Short-lived signed blob URL
  @JsonKey(
    
    name: r'uploadUrl',
    required: true,
    includeIfNull: false,
  )


  final String uploadUrl;





    @override
    bool operator ==(Object other) => identical(this, other) || other is UploadTicket &&
      other.uploadUrl == uploadUrl;

    @override
    int get hashCode =>
        uploadUrl.hashCode;

  factory UploadTicket.fromJson(Map<String, dynamic> json) => _$UploadTicketFromJson(json);

  Map<String, dynamic> toJson() => _$UploadTicketToJson(this);

  @override
  String toString() {
    return toJson().toString();
  }

}

