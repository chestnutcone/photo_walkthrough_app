// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'create_draft_request.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

CreateDraftRequest _$CreateDraftRequestFromJson(Map<String, dynamic> json) =>
    $checkedCreate('CreateDraftRequest', json, ($checkedConvert) {
      $checkKeys(json, requiredKeys: const ['contentType']);
      final val = CreateDraftRequest(
        title: $checkedConvert('title', (v) => v as String?),
        contentType: $checkedConvert(
          'contentType',
          (v) => $enumDecode(_$CreateDraftRequestContentTypeEnumEnumMap, v),
        ),
      );
      return val;
    });

Map<String, dynamic> _$CreateDraftRequestToJson(CreateDraftRequest instance) =>
    <String, dynamic>{
      'title': ?instance.title,
      'contentType':
          _$CreateDraftRequestContentTypeEnumEnumMap[instance.contentType]!,
    };

const _$CreateDraftRequestContentTypeEnumEnumMap = {
  CreateDraftRequestContentTypeEnum.imageSlashJpeg: 'image/jpeg',
  CreateDraftRequestContentTypeEnum.imageSlashPng: 'image/png',
  CreateDraftRequestContentTypeEnum.imageSlashWebp: 'image/webp',
};
