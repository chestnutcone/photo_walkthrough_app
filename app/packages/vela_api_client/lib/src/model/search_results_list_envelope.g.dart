// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'search_results_list_envelope.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

SearchResultsListEnvelope _$SearchResultsListEnvelopeFromJson(
  Map<String, dynamic> json,
) => $checkedCreate('SearchResultsListEnvelope', json, ($checkedConvert) {
  $checkKeys(json, requiredKeys: const ['success', 'data', 'error', 'page']);
  final val = SearchResultsListEnvelope(
    success: $checkedConvert('success', (v) => v as bool),
    data: $checkedConvert(
      'data',
      (v) =>
          v == null ? null : SearchResults.fromJson(v as Map<String, dynamic>),
    ),
    error: $checkedConvert(
      'error',
      (v) => v == null ? null : ErrorBody.fromJson(v as Map<String, dynamic>),
    ),
    page: $checkedConvert(
      'page',
      (v) => v == null ? null : Page.fromJson(v as Map<String, dynamic>),
    ),
  );
  return val;
});

Map<String, dynamic> _$SearchResultsListEnvelopeToJson(
  SearchResultsListEnvelope instance,
) => <String, dynamic>{
  'success': instance.success,
  'data': instance.data?.toJson(),
  'error': instance.error?.toJson(),
  'page': instance.page?.toJson(),
};
