// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'profile.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

Profile _$ProfileFromJson(
  Map<String, dynamic> json,
) => $checkedCreate('Profile', json, ($checkedConvert) {
  $checkKeys(
    json,
    requiredKeys: const [
      'id',
      'handle',
      'displayName',
      'bio',
      'followerCount',
      'followingCount',
      'workCount',
      'onboardingComplete',
    ],
  );
  final val = Profile(
    id: $checkedConvert('id', (v) => v as String),
    handle: $checkedConvert('handle', (v) => v as String),
    displayName: $checkedConvert('displayName', (v) => v as String),
    bio: $checkedConvert('bio', (v) => v as String),
    avatarUrl: $checkedConvert('avatarUrl', (v) => v as String?),
    followerCount: $checkedConvert('followerCount', (v) => (v as num).toInt()),
    followingCount: $checkedConvert(
      'followingCount',
      (v) => (v as num).toInt(),
    ),
    workCount: $checkedConvert('workCount', (v) => (v as num).toInt()),
    chapterCount: $checkedConvert('chapterCount', (v) => (v as num?)?.toInt()),
    isFollowing: $checkedConvert('isFollowing', (v) => v as bool?),
    onboardingComplete: $checkedConvert('onboardingComplete', (v) => v as bool),
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
  );
  return val;
});

Map<String, dynamic> _$ProfileToJson(Profile instance) => <String, dynamic>{
  'id': instance.id,
  'handle': instance.handle,
  'displayName': instance.displayName,
  'bio': instance.bio,
  'avatarUrl': ?instance.avatarUrl,
  'followerCount': instance.followerCount,
  'followingCount': instance.followingCount,
  'workCount': instance.workCount,
  'chapterCount': ?instance.chapterCount,
  'isFollowing': ?instance.isFollowing,
  'onboardingComplete': instance.onboardingComplete,
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
};
