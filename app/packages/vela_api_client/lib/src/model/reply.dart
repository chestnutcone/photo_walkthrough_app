//
// AUTO-GENERATED FILE, DO NOT MODIFY!
//

// ignore_for_file: unused_element
import 'package:vela_api_client/src/model/profile.dart';
import 'package:json_annotation/json_annotation.dart';

part 'reply.g.dart';


@JsonSerializable(
  checked: true,
  createToJson: true,
  disallowUnrecognizedKeys: false,
  explicitToJson: true,
)
class Reply {
  /// Returns a new [Reply] instance.
  Reply({

    required  this.id,

    required  this.workId,

    required  this.chapterIndex,

    required  this.author,

    required  this.body,

    required  this.createdAt,

     this.hiddenForOthers,
  });

  @JsonKey(
    
    name: r'id',
    required: true,
    includeIfNull: false,
  )


  final String id;



  @JsonKey(
    
    name: r'workId',
    required: true,
    includeIfNull: false,
  )


  final String workId;



  @JsonKey(
    
    name: r'chapterIndex',
    required: true,
    includeIfNull: false,
  )


  final int chapterIndex;



  @JsonKey(
    
    name: r'author',
    required: true,
    includeIfNull: false,
  )


  final Profile author;



  @JsonKey(
    
    name: r'body',
    required: true,
    includeIfNull: false,
  )


  final String body;



  @JsonKey(
    
    name: r'createdAt',
    required: true,
    includeIfNull: false,
  )


  final DateTime createdAt;



      /// True when only the author can see this reply (flagged or reported). Absent or false otherwise.
  @JsonKey(
    
    name: r'hiddenForOthers',
    required: false,
    includeIfNull: false,
  )


  final bool? hiddenForOthers;





    @override
    bool operator ==(Object other) => identical(this, other) || other is Reply &&
      other.id == id &&
      other.workId == workId &&
      other.chapterIndex == chapterIndex &&
      other.author == author &&
      other.body == body &&
      other.createdAt == createdAt &&
      other.hiddenForOthers == hiddenForOthers;

    @override
    int get hashCode =>
        id.hashCode +
        workId.hashCode +
        chapterIndex.hashCode +
        author.hashCode +
        body.hashCode +
        createdAt.hashCode +
        hiddenForOthers.hashCode;

  factory Reply.fromJson(Map<String, dynamic> json) => _$ReplyFromJson(json);

  Map<String, dynamic> toJson() => _$ReplyToJson(this);

  @override
  String toString() {
    return toJson().toString();
  }

}

