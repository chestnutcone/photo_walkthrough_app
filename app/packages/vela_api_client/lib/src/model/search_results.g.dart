// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'search_results.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

SearchResults _$SearchResultsFromJson(Map<String, dynamic> json) =>
    $checkedCreate('SearchResults', json, ($checkedConvert) {
      $checkKeys(json, requiredKeys: const ['artists', 'stories']);
      final val = SearchResults(
        artists: $checkedConvert(
          'artists',
          (v) => (v as List<dynamic>)
              .map((e) => Profile.fromJson(e as Map<String, dynamic>))
              .toList(),
        ),
        stories: $checkedConvert(
          'stories',
          (v) => (v as List<dynamic>)
              .map((e) => Work.fromJson(e as Map<String, dynamic>))
              .toList(),
        ),
      );
      return val;
    });

Map<String, dynamic> _$SearchResultsToJson(SearchResults instance) =>
    <String, dynamic>{
      'artists': instance.artists.map((e) => e.toJson()).toList(),
      'stories': instance.stories.map((e) => e.toJson()).toList(),
    };
