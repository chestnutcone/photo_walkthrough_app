//
// AUTO-GENERATED FILE, DO NOT MODIFY!
//

// ignore_for_file: unused_element
import 'package:vela_api_client/src/model/artist_summary.dart';
import 'package:vela_api_client/src/model/chapter.dart';
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

     this.intro,

     this.altText,

    required  this.isOwner,

     this.status,

     this.imageUrl,

     this.thumbUrl,

     this.feedUrl,

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


  final ArtistSummary author;



  @JsonKey(
    
    name: r'title',
    required: true,
    includeIfNull: false,
  )


  final String title;



  @JsonKey(
    
    name: r'intro',
    required: false,
    includeIfNull: false,
  )


  final String? intro;



  @JsonKey(
    
    name: r'altText',
    required: false,
    includeIfNull: false,
  )


  final String? altText;



  @JsonKey(
    
    name: r'isOwner',
    required: true,
    includeIfNull: false,
  )


  final bool isOwner;



      /// Present only for the owner and admins.
  @JsonKey(
    
    name: r'status',
    required: false,
    includeIfNull: false,
  )


  final WorkStatusEnum? status;



      /// Full-size variant
  @JsonKey(
    
    name: r'imageUrl',
    required: false,
    includeIfNull: false,
  )


  final String? imageUrl;



  @JsonKey(
    
    name: r'thumbUrl',
    required: false,
    includeIfNull: false,
  )


  final String? thumbUrl;



  @JsonKey(
    
    name: r'feedUrl',
    required: false,
    includeIfNull: false,
  )


  final String? feedUrl;



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
      other.intro == intro &&
      other.altText == altText &&
      other.isOwner == isOwner &&
      other.status == status &&
      other.imageUrl == imageUrl &&
      other.thumbUrl == thumbUrl &&
      other.feedUrl == feedUrl &&
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
        intro.hashCode +
        (altText == null ? 0 : altText.hashCode) +
        isOwner.hashCode +
        (status == null ? 0 : status.hashCode) +
        (imageUrl == null ? 0 : imageUrl.hashCode) +
        (thumbUrl == null ? 0 : thumbUrl.hashCode) +
        (feedUrl == null ? 0 : feedUrl.hashCode) +
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

/// Present only for the owner and admins.
enum WorkStatusEnum {
    /// Present only for the owner and admins.
@JsonValue(r'draft')
draft(r'draft'),
    /// Present only for the owner and admins.
@JsonValue(r'pending')
pending(r'pending'),
    /// Present only for the owner and admins.
@JsonValue(r'published')
published(r'published'),
    /// Present only for the owner and admins.
@JsonValue(r'blocked')
blocked(r'blocked'),
    /// Present only for the owner and admins.
@JsonValue(r'removed')
removed(r'removed');

const WorkStatusEnum(this.value);

final String value;

@override
String toString() => value;
}


