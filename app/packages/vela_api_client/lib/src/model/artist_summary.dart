//
// AUTO-GENERATED FILE, DO NOT MODIFY!
//

// ignore_for_file: unused_element
import 'package:json_annotation/json_annotation.dart';

part 'artist_summary.g.dart';


@JsonSerializable(
  checked: true,
  createToJson: true,
  disallowUnrecognizedKeys: false,
  explicitToJson: true,
)
class ArtistSummary {
  /// Returns a new [ArtistSummary] instance.
  ArtistSummary({

    required  this.id,

    required  this.handle,

    required  this.displayName,

     this.avatarUrl,
  });

  @JsonKey(
    
    name: r'id',
    required: true,
    includeIfNull: false,
  )


  final String id;



  @JsonKey(
    
    name: r'handle',
    required: true,
    includeIfNull: false,
  )


  final String handle;



  @JsonKey(
    
    name: r'displayName',
    required: true,
    includeIfNull: false,
  )


  final String displayName;



  @JsonKey(
    
    name: r'avatarUrl',
    required: false,
    includeIfNull: false,
  )


  final String? avatarUrl;





    @override
    bool operator ==(Object other) => identical(this, other) || other is ArtistSummary &&
      other.id == id &&
      other.handle == handle &&
      other.displayName == displayName &&
      other.avatarUrl == avatarUrl;

    @override
    int get hashCode =>
        id.hashCode +
        handle.hashCode +
        displayName.hashCode +
        (avatarUrl == null ? 0 : avatarUrl.hashCode);

  factory ArtistSummary.fromJson(Map<String, dynamic> json) => _$ArtistSummaryFromJson(json);

  Map<String, dynamic> toJson() => _$ArtistSummaryToJson(this);

  @override
  String toString() {
    return toJson().toString();
  }

}

