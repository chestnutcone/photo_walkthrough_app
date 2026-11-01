//
// AUTO-GENERATED FILE, DO NOT MODIFY!
//

// ignore_for_file: unused_element
import 'package:json_annotation/json_annotation.dart';

part 'service_item.g.dart';


@JsonSerializable(
  checked: true,
  createToJson: true,
  disallowUnrecognizedKeys: false,
  explicitToJson: true,
)
class ServiceItem {
  /// Returns a new [ServiceItem] instance.
  ServiceItem({

    required  this.title,

    required  this.description,

    required  this.price,
  });

  @JsonKey(
    
    name: r'title',
    required: true,
    includeIfNull: false,
  )


  final String title;



  @JsonKey(
    
    name: r'description',
    required: true,
    includeIfNull: false,
  )


  final String description;



  @JsonKey(
    
    name: r'price',
    required: true,
    includeIfNull: false,
  )


  final String price;





    @override
    bool operator ==(Object other) => identical(this, other) || other is ServiceItem &&
      other.title == title &&
      other.description == description &&
      other.price == price;

    @override
    int get hashCode =>
        title.hashCode +
        description.hashCode +
        price.hashCode;

  factory ServiceItem.fromJson(Map<String, dynamic> json) => _$ServiceItemFromJson(json);

  Map<String, dynamic> toJson() => _$ServiceItemToJson(this);

  @override
  String toString() {
    return toJson().toString();
  }

}

