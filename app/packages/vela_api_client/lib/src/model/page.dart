//
// AUTO-GENERATED FILE, DO NOT MODIFY!
//

// ignore_for_file: unused_element
import 'package:json_annotation/json_annotation.dart';

part 'page.g.dart';


@JsonSerializable(
  checked: true,
  createToJson: true,
  disallowUnrecognizedKeys: false,
  explicitToJson: true,
)
class Page {
  /// Returns a new [Page] instance.
  Page({

    required  this.nextCursor,
  });

  @JsonKey(
    
    name: r'nextCursor',
    required: true,
    includeIfNull: true,
  )


  final String? nextCursor;





    @override
    bool operator ==(Object other) => identical(this, other) || other is Page &&
      other.nextCursor == nextCursor;

    @override
    int get hashCode =>
        (nextCursor == null ? 0 : nextCursor.hashCode);

  factory Page.fromJson(Map<String, dynamic> json) => _$PageFromJson(json);

  Map<String, dynamic> toJson() => _$PageToJson(this);

  @override
  String toString() {
    return toJson().toString();
  }

}

