//
// AUTO-GENERATED FILE, DO NOT MODIFY!
//

// ignore_for_file: unused_element
import 'package:json_annotation/json_annotation.dart';

part 'unread_count.g.dart';


@JsonSerializable(
  checked: true,
  createToJson: true,
  disallowUnrecognizedKeys: false,
  explicitToJson: true,
)
class UnreadCount {
  /// Returns a new [UnreadCount] instance.
  UnreadCount({

    required  this.count,
  });

  @JsonKey(
    
    name: r'count',
    required: true,
    includeIfNull: false,
  )


  final int count;





    @override
    bool operator ==(Object other) => identical(this, other) || other is UnreadCount &&
      other.count == count;

    @override
    int get hashCode =>
        count.hashCode;

  factory UnreadCount.fromJson(Map<String, dynamic> json) => _$UnreadCountFromJson(json);

  Map<String, dynamic> toJson() => _$UnreadCountToJson(this);

  @override
  String toString() {
    return toJson().toString();
  }

}

