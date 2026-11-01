//
// AUTO-GENERATED FILE, DO NOT MODIFY!
//

// ignore_for_file: unused_element
import 'package:json_annotation/json_annotation.dart';

part 'chapter.g.dart';


@JsonSerializable(
  checked: true,
  createToJson: true,
  disallowUnrecognizedKeys: false,
  explicitToJson: true,
)
class Chapter {
  /// Returns a new [Chapter] instance.
  Chapter({

    required  this.x,

    required  this.y,

     this.zoom,

    required  this.title,

    required  this.body,

     this.meta,

     this.altText,

     this.replyCount,
  });

          // minimum: 0
          // maximum: 1
  @JsonKey(
    
    name: r'x',
    required: true,
    includeIfNull: false,
  )


  final double x;



          // minimum: 0
          // maximum: 1
  @JsonKey(
    
    name: r'y',
    required: true,
    includeIfNull: false,
  )


  final double y;



      /// Null means stay put; 2.4 when set by default
  @JsonKey(
    
    name: r'zoom',
    required: false,
    includeIfNull: false,
  )


  final double? zoom;



  @JsonKey(
    
    name: r'title',
    required: true,
    includeIfNull: false,
  )


  final String title;



  @JsonKey(
    
    name: r'body',
    required: true,
    includeIfNull: false,
  )


  final String body;



  @JsonKey(
    
    name: r'meta',
    required: false,
    includeIfNull: false,
  )


  final String? meta;



  @JsonKey(
    
    name: r'altText',
    required: false,
    includeIfNull: false,
  )


  final String? altText;



      /// Server-computed, ignored on input. Visible replies for the viewer.
  @JsonKey(
    
    name: r'replyCount',
    required: false,
    includeIfNull: false,
  )


  final int? replyCount;





    @override
    bool operator ==(Object other) => identical(this, other) || other is Chapter &&
      other.x == x &&
      other.y == y &&
      other.zoom == zoom &&
      other.title == title &&
      other.body == body &&
      other.meta == meta &&
      other.altText == altText &&
      other.replyCount == replyCount;

    @override
    int get hashCode =>
        x.hashCode +
        y.hashCode +
        (zoom == null ? 0 : zoom.hashCode) +
        title.hashCode +
        body.hashCode +
        (meta == null ? 0 : meta.hashCode) +
        (altText == null ? 0 : altText.hashCode) +
        replyCount.hashCode;

  factory Chapter.fromJson(Map<String, dynamic> json) => _$ChapterFromJson(json);

  Map<String, dynamic> toJson() => _$ChapterToJson(this);

  @override
  String toString() {
    return toJson().toString();
  }

}

