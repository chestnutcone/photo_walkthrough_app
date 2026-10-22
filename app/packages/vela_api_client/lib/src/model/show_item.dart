//
// AUTO-GENERATED FILE, DO NOT MODIFY!
//

// ignore_for_file: unused_element
import 'package:json_annotation/json_annotation.dart';

part 'show_item.g.dart';


@JsonSerializable(
  checked: true,
  createToJson: true,
  disallowUnrecognizedKeys: false,
  explicitToJson: true,
)
class ShowItem {
  /// Returns a new [ShowItem] instance.
  ShowItem({

    required  this.year,

    required  this.text,
  });

  @JsonKey(
    
    name: r'year',
    required: true,
    includeIfNull: false,
  )


  final String year;



  @JsonKey(
    
    name: r'text',
    required: true,
    includeIfNull: false,
  )


  final String text;





    @override
    bool operator ==(Object other) => identical(this, other) || other is ShowItem &&
      other.year == year &&
      other.text == text;

    @override
    int get hashCode =>
        year.hashCode +
        text.hashCode;

  factory ShowItem.fromJson(Map<String, dynamic> json) => _$ShowItemFromJson(json);

  Map<String, dynamic> toJson() => _$ShowItemToJson(this);

  @override
  String toString() {
    return toJson().toString();
  }

}

