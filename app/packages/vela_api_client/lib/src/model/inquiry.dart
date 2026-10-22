//
// AUTO-GENERATED FILE, DO NOT MODIFY!
//

// ignore_for_file: unused_element
import 'package:json_annotation/json_annotation.dart';

part 'inquiry.g.dart';


@JsonSerializable(
  checked: true,
  createToJson: true,
  disallowUnrecognizedKeys: false,
  explicitToJson: true,
)
class Inquiry {
  /// Returns a new [Inquiry] instance.
  Inquiry({

    required  this.id,

    required  this.artistId,

     this.workId,

    required  this.message,

    required  this.createdAt,
  });

  @JsonKey(
    
    name: r'id',
    required: true,
    includeIfNull: false,
  )


  final String id;



  @JsonKey(
    
    name: r'artistId',
    required: true,
    includeIfNull: false,
  )


  final String artistId;



  @JsonKey(
    
    name: r'workId',
    required: false,
    includeIfNull: false,
  )


  final String? workId;



  @JsonKey(
    
    name: r'message',
    required: true,
    includeIfNull: false,
  )


  final String message;



  @JsonKey(
    
    name: r'createdAt',
    required: true,
    includeIfNull: false,
  )


  final DateTime createdAt;





    @override
    bool operator ==(Object other) => identical(this, other) || other is Inquiry &&
      other.id == id &&
      other.artistId == artistId &&
      other.workId == workId &&
      other.message == message &&
      other.createdAt == createdAt;

    @override
    int get hashCode =>
        id.hashCode +
        artistId.hashCode +
        (workId == null ? 0 : workId.hashCode) +
        message.hashCode +
        createdAt.hashCode;

  factory Inquiry.fromJson(Map<String, dynamic> json) => _$InquiryFromJson(json);

  Map<String, dynamic> toJson() => _$InquiryToJson(this);

  @override
  String toString() {
    return toJson().toString();
  }

}

