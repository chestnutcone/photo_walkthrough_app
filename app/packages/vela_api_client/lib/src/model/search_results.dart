//
// AUTO-GENERATED FILE, DO NOT MODIFY!
//

// ignore_for_file: unused_element
import 'package:vela_api_client/src/model/work.dart';
import 'package:vela_api_client/src/model/profile.dart';
import 'package:json_annotation/json_annotation.dart';

part 'search_results.g.dart';


@JsonSerializable(
  checked: true,
  createToJson: true,
  disallowUnrecognizedKeys: false,
  explicitToJson: true,
)
class SearchResults {
  /// Returns a new [SearchResults] instance.
  SearchResults({

    required  this.artists,

    required  this.stories,
  });

  @JsonKey(
    
    name: r'artists',
    required: true,
    includeIfNull: false,
  )


  final List<Profile> artists;



  @JsonKey(
    
    name: r'stories',
    required: true,
    includeIfNull: false,
  )


  final List<Work> stories;





    @override
    bool operator ==(Object other) => identical(this, other) || other is SearchResults &&
      other.artists == artists &&
      other.stories == stories;

    @override
    int get hashCode =>
        artists.hashCode +
        stories.hashCode;

  factory SearchResults.fromJson(Map<String, dynamic> json) => _$SearchResultsFromJson(json);

  Map<String, dynamic> toJson() => _$SearchResultsToJson(this);

  @override
  String toString() {
    return toJson().toString();
  }

}

