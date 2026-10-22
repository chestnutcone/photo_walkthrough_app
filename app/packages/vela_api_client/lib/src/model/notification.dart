//
// AUTO-GENERATED FILE, DO NOT MODIFY!
//

// ignore_for_file: unused_element
import 'package:vela_api_client/src/model/profile.dart';
import 'package:json_annotation/json_annotation.dart';

part 'notification.g.dart';


@JsonSerializable(
  checked: true,
  createToJson: true,
  disallowUnrecognizedKeys: false,
  explicitToJson: true,
)
class Notification {
  /// Returns a new [Notification] instance.
  Notification({

    required  this.id,

    required  this.type,

    required  this.actor,

     this.workId,

     this.workTitle,

     this.chapterIndex,

     this.replyId,

    required  this.createdAt,

    required  this.read,

     this.count,

     this.ids,
  });

  @JsonKey(
    
    name: r'id',
    required: true,
    includeIfNull: false,
  )


  final String id;



  @JsonKey(
    
    name: r'type',
    required: true,
    includeIfNull: false,
  )


  final NotificationTypeEnum type;



  @JsonKey(
    
    name: r'actor',
    required: true,
    includeIfNull: false,
  )


  final Profile actor;



  @JsonKey(
    
    name: r'workId',
    required: false,
    includeIfNull: false,
  )


  final String? workId;



      /// Title of the story the notification is about
  @JsonKey(
    
    name: r'workTitle',
    required: false,
    includeIfNull: false,
  )


  final String? workTitle;



  @JsonKey(
    
    name: r'chapterIndex',
    required: false,
    includeIfNull: false,
  )


  final int? chapterIndex;



  @JsonKey(
    
    name: r'replyId',
    required: false,
    includeIfNull: false,
  )


  final String? replyId;



  @JsonKey(
    
    name: r'createdAt',
    required: true,
    includeIfNull: false,
  )


  final DateTime createdAt;



  @JsonKey(
    
    name: r'read',
    required: true,
    includeIfNull: false,
  )


  final bool read;



      /// Rows folded into this item. Consecutive unread replies on one chapter collapse into one item (render \"3 new replies on <title>\"); `id` and `actor` are the latest row's. 1 for every other item.
          // minimum: 1
  @JsonKey(
    
    name: r'count',
    required: false,
    includeIfNull: false,
  )


  final int? count;



      /// Ids of every row folded into this item (pass them to markRead)
  @JsonKey(
    
    name: r'ids',
    required: false,
    includeIfNull: false,
  )


  final List<String>? ids;





    @override
    bool operator ==(Object other) => identical(this, other) || other is Notification &&
      other.id == id &&
      other.type == type &&
      other.actor == actor &&
      other.workId == workId &&
      other.workTitle == workTitle &&
      other.chapterIndex == chapterIndex &&
      other.replyId == replyId &&
      other.createdAt == createdAt &&
      other.read == read &&
      other.count == count &&
      other.ids == ids;

    @override
    int get hashCode =>
        id.hashCode +
        type.hashCode +
        actor.hashCode +
        (workId == null ? 0 : workId.hashCode) +
        (workTitle == null ? 0 : workTitle.hashCode) +
        (chapterIndex == null ? 0 : chapterIndex.hashCode) +
        (replyId == null ? 0 : replyId.hashCode) +
        createdAt.hashCode +
        read.hashCode +
        count.hashCode +
        ids.hashCode;

  factory Notification.fromJson(Map<String, dynamic> json) => _$NotificationFromJson(json);

  Map<String, dynamic> toJson() => _$NotificationToJson(this);

  @override
  String toString() {
    return toJson().toString();
  }

}


enum NotificationTypeEnum {
@JsonValue(r'follow')
follow(r'follow'),
@JsonValue(r'reply')
reply(r'reply'),
@JsonValue(r'new_story')
newStory(r'new_story'),
@JsonValue(r'inquiry')
inquiry(r'inquiry'),
@JsonValue(r'moderation')
moderation(r'moderation');

const NotificationTypeEnum(this.value);

final String value;

@override
String toString() => value;
}


