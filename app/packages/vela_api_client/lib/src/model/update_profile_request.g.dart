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
  'avatarUploaded': ?instance.avatarUploaded,
};
