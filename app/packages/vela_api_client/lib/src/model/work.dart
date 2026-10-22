//
// AUTO-GENERATED FILE, DO NOT MODIFY!
//

// ignore_for_file: unused_element
import 'package:vela_api_client/src/model/chapter.dart';
import 'package:vela_api_client/src/model/profile.dart';
import 'package:json_annotation/json_annotation.dart';

part 'work.g.dart';


@JsonSerializable(
  checked: true,
  createToJson: true,
  disallowUnrecognizedKeys: false,
  explicitToJson: true,
)
class Work {
  /// Returns a new [Work] instance.
  Work({

    required  this.id,

    required  this.author,

    required  this.title,

    required  this.status,

     this.imageUrl,

     this.imageWidth,

     this.imageHeight,

    required  this.chapters,

    required  this.createdAt,

     this.publishedAt,

     this.draftExpiresAt,

    required  this.shareUrl,
  });

  @JsonKey(
    
    name: r'id',
    required: true,
    includeIfNull: false,
  )


  final String id;



  @JsonKey(
    
    name: r'author',
    required: true,
    includeIfNull: false,
  )


  final Profile author;



  @JsonKey(
    
    name: r'title',
    required: true,
    includeIfNull: false,
  )


  final String title;



  @JsonKey(
    
    name: r'status',
    required: true,
    includeIfNull: false,
  )


  final WorkStatusEnum status;



  @JsonKey(
    
    name: r'imageUrl',
    required: false,
    includeIfNull: false,
  )


  final String? imageUrl;



  @JsonKey(
    
    name: r'imageWidth',
    required: false,
    includeIfNull: false,
  )


  final int? imageWidth;



  @JsonKey(
    
    name: r'imageHeight',
    required: false,
    includeIfNull: false,
  )


  final int? imageHeight;



  @JsonKey(
    
    name: r'chapters',
    required: true,
    includeIfNull: false,
  )


  final List<Chapter> chapters;



  @JsonKey(
    
    name: r'createdAt',
    required: true,
    includeIfNull: false,
  )


  final DateTime createdAt;



  @JsonKey(
    
    name: r'publishedAt',
    required: false,
    includeIfNull: false,
  )


  final DateTime? publishedAt;



  @JsonKey(
    
    name: r'draftExpiresAt',
    required: false,
    includeIfNull: false,
  )


  final DateTime? draftExpiresAt;



  @JsonKey(
    
    name: r'shareUrl',
    required: true,
    includeIfNull: false,
  )


  final String shareUrl;





    @override
    bool operator ==(Object other) => identical(this, other) || other is Work &&
      other.id == id &&
      other.author == author &&
      other.title == title &&
      other.status == status &&
      other.imageUrl == imageUrl &&
      other.imageWidth == imageWidth &&
      other.imageHeight == imageHeight &&
      other.chapters == chapters &&
      other.createdAt == createdAt &&
      other.publishedAt == publishedAt &&
      other.draftExpiresAt == draftExpiresAt &&
      other.shareUrl == shareUrl;

    @override
    int get hashCode =>
        id.hashCode +
        author.hashCode +
        title.hashCode +
        status.hashCode +
        (imageUrl == null ? 0 : imageUrl.hashCode) +
        (imageWidth == null ? 0 : imageWidth.hashCode) +
        (imageHeight == null ? 0 : imageHeight.hashCode) +
        chapters.hashCode +
        createdAt.hashCode +
        (publishedAt == null ? 0 : publishedAt.hashCode) +
        (draftExpiresAt == null ? 0 : draftExpiresAt.hashCode) +
        shareUrl.hashCode;

  factory Work.fromJson(Map<String, dynamic> json) => _$WorkFromJson(json);

  Map<String, dynamic> toJson() => _$WorkToJson(this);

  @override
  String toString() {
    return toJson().toString();
  }

}


enum WorkStatusEnum {
@JsonValue(r'draft')
draft(r'draft'),
@JsonValue(r'pending')
pending(r'pending'),
@JsonValue(r'published')
published(r'published'),
@JsonValue(r'blocked')
blocked(r'blocked'),
@JsonValue(r'removed')
removed(r'removed');

const WorkStatusEnum(this.value);

final String value;

@override
String toString() => value;
}


