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
    isFollowing: $checkedConvert('isFollowing', (v) => v as bool?),
    onboardingComplete: $checkedConvert('onboardingComplete', (v) => v as bool),
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
  'isFollowing': ?instance.isFollowing,
  'onboardingComplete': instance.onboardingComplete,
};
