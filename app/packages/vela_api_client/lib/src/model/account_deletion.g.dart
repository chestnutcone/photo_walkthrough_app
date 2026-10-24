// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'account_deletion.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

AccountDeletion _$AccountDeletionFromJson(Map<String, dynamic> json) =>
    $checkedCreate('AccountDeletion', json, ($checkedConvert) {
      $checkKeys(json, requiredKeys: const ['state', 'completesBy']);
      final val = AccountDeletion(
        state: $checkedConvert(
          'state',
          (v) => $enumDecode(_$AccountDeletionStateEnumEnumMap, v),
        ),
        completesBy: $checkedConvert(
          'completesBy',
          (v) => v == null ? null : DateTime.parse(v as String),
        ),
      );
      return val;
    });

Map<String, dynamic> _$AccountDeletionToJson(AccountDeletion instance) =>
    <String, dynamic>{
      'state': _$AccountDeletionStateEnumEnumMap[instance.state]!,
      'completesBy': instance.completesBy?.toIso8601String(),
    };

const _$AccountDeletionStateEnumEnumMap = {
  AccountDeletionStateEnum.scheduled: 'scheduled',
  AccountDeletionStateEnum.deferredReview: 'deferred_review',
};
