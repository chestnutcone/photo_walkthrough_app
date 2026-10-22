// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'me.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

Me _$MeFromJson(Map<String, dynamic> json) => $checkedCreate('Me', json, (
  $checkedConvert,
) {
  $checkKeys(
    json,
    requiredKeys: const [
      'id',
      'profile',
      'onboardingComplete',
      'tier',
      'usedWorks',
    ],
  );
  final val = Me(
    id: $checkedConvert('id', (v) => v as String),
    profile: $checkedConvert(
      'profile',
      (v) => v == null ? null : Profile.fromJson(v as Map<String, dynamic>),
    ),
    onboardingComplete: $checkedConvert('onboardingComplete', (v) => v as bool),
    tier: $checkedConvert(
      'tier',
      (v) => MeTier.fromJson(v as Map<String, dynamic>),
    ),
    usedWorks: $checkedConvert('usedWorks', (v) => (v as num).toInt()),
  );
  return val;
});

Map<String, dynamic> _$MeToJson(Me instance) => <String, dynamic>{
  'id': instance.id,
  'profile': instance.profile?.toJson(),
  'onboardingComplete': instance.onboardingComplete,
  'tier': instance.tier.toJson(),
  'usedWorks': instance.usedWorks,
};
