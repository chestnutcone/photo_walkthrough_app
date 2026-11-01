//
// AUTO-GENERATED FILE, DO NOT MODIFY!
//

// ignore_for_file: unused_element
import 'package:json_annotation/json_annotation.dart';

part 'me_tier.g.dart';


@JsonSerializable(
  checked: true,
  createToJson: true,
  disallowUnrecognizedKeys: false,
  explicitToJson: true,
)
class MeTier {
  /// Returns a new [MeTier] instance.
  MeTier({

    required  this.name,

    required  this.maxWorks,
  });

  @JsonKey(
    
    name: r'name',
    required: true,
    includeIfNull: false,
  )


  final String name;



  @JsonKey(
    
    name: r'maxWorks',
    required: true,
    includeIfNull: false,
  )


  final int maxWorks;





    @override
    bool operator ==(Object other) => identical(this, other) || other is MeTier &&
      other.name == name &&
      other.maxWorks == maxWorks;

    @override
    int get hashCode =>
        name.hashCode +
        maxWorks.hashCode;

  factory MeTier.fromJson(Map<String, dynamic> json) => _$MeTierFromJson(json);

  Map<String, dynamic> toJson() => _$MeTierToJson(this);

  @override
  String toString() {
    return toJson().toString();
  }

}

