//
// AUTO-GENERATED FILE, DO NOT MODIFY!
//

// ignore_for_file: unused_element
import 'package:json_annotation/json_annotation.dart';

part 'export_job.g.dart';


@JsonSerializable(
  checked: true,
  createToJson: true,
  disallowUnrecognizedKeys: false,
  explicitToJson: true,
)
class ExportJob {
  /// Returns a new [ExportJob] instance.
  ExportJob({

    required  this.id,

    required  this.status,

     this.downloadUrl,

    required  this.createdAt,

     this.expiresAt,
  });

  @JsonKey(
    
    name: r'id',
    required: true,
    includeIfNull: false,
  )


  final String id;



      /// expired = past its 7-day life; request a new export
  @JsonKey(
    
    name: r'status',
    required: true,
    includeIfNull: false,
  )


  final ExportJobStatusEnum status;



      /// Read-only signed URL for the zip, valid for at most 15 minutes; present only while status is ready (fetch the export again for a fresh one)
  @JsonKey(
    
    name: r'downloadUrl',
    required: false,
    includeIfNull: false,
  )


  final String? downloadUrl;



  @JsonKey(
    
    name: r'createdAt',
    required: true,
    includeIfNull: false,
  )


  final DateTime createdAt;



  @JsonKey(
    
    name: r'expiresAt',
    required: false,
    includeIfNull: false,
  )


  final DateTime? expiresAt;





    @override
    bool operator ==(Object other) => identical(this, other) || other is ExportJob &&
      other.id == id &&
      other.status == status &&
      other.downloadUrl == downloadUrl &&
      other.createdAt == createdAt &&
      other.expiresAt == expiresAt;

    @override
    int get hashCode =>
        id.hashCode +
        status.hashCode +
        (downloadUrl == null ? 0 : downloadUrl.hashCode) +
        createdAt.hashCode +
        (expiresAt == null ? 0 : expiresAt.hashCode);

  factory ExportJob.fromJson(Map<String, dynamic> json) => _$ExportJobFromJson(json);

  Map<String, dynamic> toJson() => _$ExportJobToJson(this);

  @override
  String toString() {
    return toJson().toString();
  }

}

/// expired = past its 7-day life; request a new export
enum ExportJobStatusEnum {
    /// expired = past its 7-day life; request a new export
@JsonValue(r'pending')
pending(r'pending'),
    /// expired = past its 7-day life; request a new export
@JsonValue(r'ready')
ready(r'ready'),
    /// expired = past its 7-day life; request a new export
@JsonValue(r'failed')
failed(r'failed'),
    /// expired = past its 7-day life; request a new export
@JsonValue(r'expired')
expired(r'expired');

const ExportJobStatusEnum(this.value);

final String value;

@override
String toString() => value;
}


