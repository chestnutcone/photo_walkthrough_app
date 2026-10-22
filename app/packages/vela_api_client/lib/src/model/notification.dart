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

    required  this.createdAt,

    required  this.read,
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





    @override
    bool operator ==(Object other) => identical(this, other) || other is Notification &&
      other.id == id &&
      other.type == type &&
      other.actor == actor &&
      other.workId == workId &&
      other.createdAt == createdAt &&
      other.read == read;

    @override
    int get hashCode =>
        id.hashCode +
        type.hashCode +
        actor.hashCode +
        (workId == null ? 0 : workId.hashCode) +
        createdAt.hashCode +
        read.hashCode;

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
@JsonValue(r'inquiry')
inquiry(r'inquiry'),
@JsonValue(r'moderation')
moderation(r'moderation');

const NotificationTypeEnum(this.value);

final String value;

@override
String toString() => value;
}


