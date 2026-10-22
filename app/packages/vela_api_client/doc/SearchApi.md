# vela_api_client.api.SearchApi

## Load the API package
```dart
import 'package:vela_api_client/api.dart';
```

All URIs are relative to *http://localhost:5080*

Method | HTTP request | Description
------------- | ------------- | -------------
[**search**](SearchApi.md#search) | **GET** /v1/search | Search artists or stories


# **search**
> SearchResultsListEnvelope search(q, type, cursor, limit)

Search artists or stories

### Example
```dart
import 'package:vela_api_client/api.dart';

final api = VelaApiClient().getSearchApi();
final String q = q_example; // String | Search text
final String type = type_example; // String | What to search
final String cursor = cursor_example; // String | Opaque cursor from the previous page
final int limit = 56; // int | Page size, 1 to 50, default 20

try {
    final response = api.search(q, type, cursor, limit);
    print(response);
} catch on DioException (e) {
    print('Exception when calling SearchApi->search: $e\n');
}
```

### Parameters

Name | Type | Description  | Notes
------------- | ------------- | ------------- | -------------
 **q** | **String**| Search text | 
 **type** | **String**| What to search | [optional] 
 **cursor** | **String**| Opaque cursor from the previous page | [optional] 
 **limit** | **int**| Page size, 1 to 50, default 20 | [optional] [default to 20]

### Return type

[**SearchResultsListEnvelope**](SearchResultsListEnvelope.md)

### Authorization

No authorization required

### HTTP request headers

 - **Content-Type**: Not defined
 - **Accept**: application/json

[[Back to top]](#) [[Back to API list]](../README.md#documentation-for-api-endpoints) [[Back to Model list]](../README.md#documentation-for-models) [[Back to README]](../README.md)

