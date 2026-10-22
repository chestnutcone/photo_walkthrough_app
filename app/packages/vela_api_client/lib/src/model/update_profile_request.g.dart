// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'update_profile_request.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

UpdateProfileRequest _$UpdateProfileRequestFromJson(
  Map<String, dynamic> json,
) => $checkedCreate('UpdateProfileRequest', json, ($checkedConvert) {
  final val = UpdateProfileRequest(
    displayName: $checkedConvert('displayName', (v) => v as String?),
    handle: $checkedConvert('handle', (v) => v as String?),
    bio: $checkedConvert('bio', (v) => v as String?),
    city: $checkedConvert('city', (v) => v as String?),
    discipline: $checkedConvert('discipline', (v) => v as String?),
    availability: $checkedConvert('availability', (v) => v as String?),
    website: $checkedConvert('website', (v) => v as String?),
    social: $checkedConvert('social', (v) => v as String?),
    studio: $checkedConvert('studio', (v) => v as String?),
    replyTime: $checkedConvert('replyTime', (v) => v as String?),
    publicEmail: $checkedConvert('publicEmail', (v) => v as String?),
    services: $checkedConvert(
      'services',
      (v) => (v as List<dynamic>?)
          ?.map((e) => ServiceItem.fromJson(e as Map<String, dynamic>))
          .toList(),
    ),
    shows: $checkedConvert(
      'shows',
      (v) => (v as List<dynamic>?)
          ?.map((e) => ShowItem.fromJson(e as Map<String, dynamic>))
          .toList(),
    ),
    avatarUploaded: $checkedConvert('avatarUploaded', (v) => v as bool?),
  );
  return val;
});

Map<String, dynamic> _$UpdateProfileRequestToJson(
  UpdateProfileRequest instance,
) => <String, dynamic>{
  'displayName': ?instance.displayName,
  'handle': ?instance.handle,
  'bio': ?instance.bio,
  'city': ?instance.city,
  'discipline': ?instance.discipline,
  'availability': ?instance.availability,
  'website': ?instance.website,
  'social': ?instance.social,
  'studio': ?instance.studio,
  'replyTime': ?instance.replyTime,
  'publicEmail': ?instance.publicEmail,
  'services': ?instance.services?.map((e) => e.toJson()).toList(),
  'shows': ?instance.shows?.map((e) => e.toJson()).toList(),
  'avatarUploaded': ?instance.avatarUploaded,
};
