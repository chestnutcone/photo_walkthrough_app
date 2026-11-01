// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'complete_onboarding_request.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

CompleteOnboardingRequest _$CompleteOnboardingRequestFromJson(
  Map<String, dynamic> json,
) => $checkedCreate('CompleteOnboardingRequest', json, ($checkedConvert) {
  $checkKeys(
    json,
    requiredKeys: const [
      'displayName',
      'handle',
      'ageConfirmed',
      'termsAccepted',
      'followArtistIds',
    ],
  );
  final val = CompleteOnboardingRequest(
    displayName: $checkedConvert('displayName', (v) => v as String),
    handle: $checkedConvert('handle', (v) => v as String),
    ageConfirmed: $checkedConvert('ageConfirmed', (v) => v as bool),
    termsAccepted: $checkedConvert('termsAccepted', (v) => v as bool),
    followArtistIds: $checkedConvert(
      'followArtistIds',
      (v) => (v as List<dynamic>).map((e) => e as String).toList(),
    ),
  );
  return val;
});

Map<String, dynamic> _$CompleteOnboardingRequestToJson(
  CompleteOnboardingRequest instance,
) => <String, dynamic>{
  'displayName': instance.displayName,
  'handle': instance.handle,
  'ageConfirmed': instance.ageConfirmed,
  'termsAccepted': instance.termsAccepted,
  'followArtistIds': instance.followArtistIds,
};
