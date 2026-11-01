// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'mark_read_request.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

MarkReadRequest _$MarkReadRequestFromJson(Map<String, dynamic> json) =>
    $checkedCreate('MarkReadRequest', json, ($checkedConvert) {
      final val = MarkReadRequest(
        ids: $checkedConvert(
          'ids',
          (v) => (v as List<dynamic>?)?.map((e) => e as String).toList(),
        ),
        all: $checkedConvert('all', (v) => v as bool?),
      );
      return val;
    });

Map<String, dynamic> _$MarkReadRequestToJson(MarkReadRequest instance) =>
    <String, dynamic>{'ids': ?instance.ids, 'all': ?instance.all};
