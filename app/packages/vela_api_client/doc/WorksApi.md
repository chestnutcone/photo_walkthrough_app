# vela_api_client.api.WorksApi

## Load the API package
```dart
import 'package:vela_api_client/api.dart';
```

All URIs are relative to *http://localhost:5080*

Method | HTTP request | Description
------------- | ------------- | -------------
[**createDraft**](WorksApi.md#createdraft) | **POST** /v1/works | Create a draft and get an upload URL
[**deleteWork**](WorksApi.md#deletework) | **DELETE** /v1/works/{id} | Delete a work
[**getFeed**](WorksApi.md#getfeed) | **GET** /v1/feed | Following or discover feed
[**getWork**](WorksApi.md#getwork) | **GET** /v1/works/{id} | Get a work
[**publishWork**](WorksApi.md#publishwork) | **POST** /v1/works/{id}/publish | Publish a draft
[**updateWork**](WorksApi.md#updatework) | **PUT** /v1/works/{id} | Update a work&#39;s title and chapters


# **createDraft**
> CreateDraftResultEnvelope createDraft(createDraftRequest)

Create a draft and get an upload URL

Counts against the tier's story limit. Errors: 409 quota_exceeded (message includes the limit) when the account is at its limit; 409 onboarding_required before onboarding.

### Example
```dart
import 'package:vela_api_client/api.dart';

final api = VelaApiClient().getWorksApi();
final CreateDraftRequest createDraftRequest = ; // CreateDraftRequest | 

try {
    final response = api.createDraft(createDraftRequest);
    print(response);
} catch on DioException (e) {
    print('Exception when calling WorksApi->createDraft: $e\n');
}
```

### Parameters

Name | Type | Description  | Notes
------------- | ------------- | ------------- | -------------
 **createDraftRequest** | [**CreateDraftRequest**](CreateDraftRequest.md)|  | 

### Return type

[**CreateDraftResultEnvelope**](CreateDraftResultEnvelope.md)

### Authorization

[bearerAuth](../README.md#bearerAuth)

### HTTP request headers

 - **Content-Type**: application/json
 - **Accept**: application/json

[[Back to top]](#) [[Back to API list]](../README.md#documentation-for-api-endpoints) [[Back to Model list]](../README.md#documentation-for-models) [[Back to README]](../README.md)

# **deleteWork**
> Envelope deleteWork(id)

Delete a work

### Example
```dart
import 'package:vela_api_client/api.dart';

final api = VelaApiClient().getWorksApi();
final String id = 38400000-8cf0-11bd-b23e-10b96e4ef00d; // String | Resource id

try {
    final response = api.deleteWork(id);
    print(response);
} catch on DioException (e) {
    print('Exception when calling WorksApi->deleteWork: $e\n');
}
```

### Parameters

Name | Type | Description  | Notes
------------- | ------------- | ------------- | -------------
 **id** | **String**| Resource id | 

### Return type

[**Envelope**](Envelope.md)

### Authorization

[bearerAuth](../README.md#bearerAuth)

### HTTP request headers

 - **Content-Type**: Not defined
 - **Accept**: application/json

[[Back to top]](#) [[Back to API list]](../README.md#documentation-for-api-endpoints) [[Back to Model list]](../README.md#documentation-for-models) [[Back to README]](../README.md)

# **getFeed**
> WorkListEnvelope getFeed(tab, cursor, limit)

Following or discover feed

Public. `discover` is newest first. `following` lists works by followed artists first, then everyone else's, each newest first; a guest, or a viewer who follows nobody, gets `discover`. Invalid cursor: 400 invalid_cursor.

### Example
```dart
import 'package:vela_api_client/api.dart';

final api = VelaApiClient().getWorksApi();
final String tab = tab_example; // String | Feed tab
final String cursor = cursor_example; // String | Opaque cursor from the previous page
final int limit = 56; // int | Page size, 1 to 50, default 20

try {
    final response = api.getFeed(tab, cursor, limit);
    print(response);
} catch on DioException (e) {
    print('Exception when calling WorksApi->getFeed: $e\n');
}
```

### Parameters

Name | Type | Description  | Notes
------------- | ------------- | ------------- | -------------
 **tab** | **String**| Feed tab | 
 **cursor** | **String**| Opaque cursor from the previous page | [optional] 
 **limit** | **int**| Page size, 1 to 50, default 20 | [optional] [default to 20]

### Return type

[**WorkListEnvelope**](WorkListEnvelope.md)

### Authorization

No authorization required

### HTTP request headers

 - **Content-Type**: Not defined
 - **Accept**: application/json

[[Back to top]](#) [[Back to API list]](../README.md#documentation-for-api-endpoints) [[Back to Model list]](../README.md#documentation-for-models) [[Back to README]](../README.md)

# **getWork**
> WorkEnvelope getWork(id)

Get a work

### Example
```dart
import 'package:vela_api_client/api.dart';

final api = VelaApiClient().getWorksApi();
final String id = 38400000-8cf0-11bd-b23e-10b96e4ef00d; // String | Resource id

try {
    final response = api.getWork(id);
    print(response);
} catch on DioException (e) {
    print('Exception when calling WorksApi->getWork: $e\n');
}
```

### Parameters

Name | Type | Description  | Notes
------------- | ------------- | ------------- | -------------
 **id** | **String**| Resource id | 

### Return type

[**WorkEnvelope**](WorkEnvelope.md)

### Authorization

No authorization required

### HTTP request headers

 - **Content-Type**: Not defined
 - **Accept**: application/json

[[Back to top]](#) [[Back to API list]](../README.md#documentation-for-api-endpoints) [[Back to Model list]](../README.md#documentation-for-models) [[Back to README]](../README.md)

# **publishWork**
> WorkEnvelope publishWork(id)

Publish a draft

### Example
```dart
import 'package:vela_api_client/api.dart';

final api = VelaApiClient().getWorksApi();
final String id = 38400000-8cf0-11bd-b23e-10b96e4ef00d; // String | Resource id

try {
    final response = api.publishWork(id);
    print(response);
} catch on DioException (e) {
    print('Exception when calling WorksApi->publishWork: $e\n');
}
```

### Parameters

Name | Type | Description  | Notes
------------- | ------------- | ------------- | -------------
 **id** | **String**| Resource id | 

### Return type

[**WorkEnvelope**](WorkEnvelope.md)

### Authorization

[bearerAuth](../README.md#bearerAuth)

### HTTP request headers

 - **Content-Type**: Not defined
 - **Accept**: application/json

[[Back to top]](#) [[Back to API list]](../README.md#documentation-for-api-endpoints) [[Back to Model list]](../README.md#documentation-for-models) [[Back to README]](../README.md)

# **updateWork**
> WorkEnvelope updateWork(id, updateWorkRequest)

Update a work's title and chapters

### Example
```dart
import 'package:vela_api_client/api.dart';

final api = VelaApiClient().getWorksApi();
final String id = 38400000-8cf0-11bd-b23e-10b96e4ef00d; // String | Resource id
final UpdateWorkRequest updateWorkRequest = ; // UpdateWorkRequest | 

try {
    final response = api.updateWork(id, updateWorkRequest);
    print(response);
} catch on DioException (e) {
    print('Exception when calling WorksApi->updateWork: $e\n');
}
```

### Parameters

Name | Type | Description  | Notes
------------- | ------------- | ------------- | -------------
 **id** | **String**| Resource id | 
 **updateWorkRequest** | [**UpdateWorkRequest**](UpdateWorkRequest.md)|  | 

### Return type

[**WorkEnvelope**](WorkEnvelope.md)

### Authorization

[bearerAuth](../README.md#bearerAuth)

### HTTP request headers

 - **Content-Type**: application/json
 - **Accept**: application/json

[[Back to top]](#) [[Back to API list]](../README.md#documentation-for-api-endpoints) [[Back to Model list]](../README.md#documentation-for-models) [[Back to README]](../README.md)

