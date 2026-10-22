// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'artist_summary.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

ArtistSummary _$ArtistSummaryFromJson(Map<String, dynamic> json) =>
    $checkedCreate('ArtistSummary', json, ($checkedConvert) {
      $checkKeys(json, requiredKeys: const ['id', 'handle', 'displayName']);
      final val = ArtistSummary(
        id: $checkedConvert('id', (v) => v as String),
        handle: $checkedConvert('handle', (v) => v as String),
        displayName: $checkedConvert('displayName', (v) => v as String),
        avatarUrl: $checkedConvert('avatarUrl', (v) => v as String?),
      );
      return val;
    });

Map<String, dynamic> _$ArtistSummaryToJson(ArtistSummary instance) =>
    <String, dynamic>{
      'id': instance.id,
      'handle': instance.handle,
      'displayName': instance.displayName,
      'avatarUrl': ?instance.avatarUrl,
    };
