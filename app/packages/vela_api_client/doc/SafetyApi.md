# vela_api_client.api.SafetyApi

## Load the API package
```dart
import 'package:vela_api_client/api.dart';
```

All URIs are relative to *http://localhost:5080*

Method | HTTP request | Description
------------- | ------------- | -------------
[**block**](SafetyApi.md#block) | **PUT** /v1/blocks/{userId} | Block a user
[**report**](SafetyApi.md#report) | **POST** /v1/reports | Report a work or reply
[**unblock**](SafetyApi.md#unblock) | **DELETE** /v1/blocks/{userId} | Unblock a user


# **block**
> Envelope block(userId)

Block a user

### Example
```dart
import 'package:vela_api_client/api.dart';

final api = VelaApiClient().getSafetyApi();
final String userId = 38400000-8cf0-11bd-b23e-10b96e4ef00d; // String | User id

try {
    final response = api.block(userId);
    print(response);
} catch on DioException (e) {
    print('Exception when calling SafetyApi->block: $e\n');
}
```

### Parameters

Name | Type | Description  | Notes
------------- | ------------- | ------------- | -------------
 **userId** | **String**| User id | 

### Return type

[**Envelope**](Envelope.md)

### Authorization

[bearerAuth](../README.md#bearerAuth)

### HTTP request headers

 - **Content-Type**: Not defined
 - **Accept**: application/json

[[Back to top]](#) [[Back to API list]](../README.md#documentation-for-api-endpoints) [[Back to Model list]](../README.md#documentation-for-models) [[Back to README]](../README.md)

# **report**
> Envelope report(reportRequest)

Report a work or reply

### Example
```dart
import 'package:vela_api_client/api.dart';

final api = VelaApiClient().getSafetyApi();
final ReportRequest reportRequest = ; // ReportRequest | 

try {
    final response = api.report(reportRequest);
    print(response);
} catch on DioException (e) {
    print('Exception when calling SafetyApi->report: $e\n');
}
```

### Parameters

Name | Type | Description  | Notes
------------- | ------------- | ------------- | -------------
 **reportRequest** | [**ReportRequest**](ReportRequest.md)|  | 

### Return type

[**Envelope**](Envelope.md)

### Authorization

[bearerAuth](../README.md#bearerAuth)

### HTTP request headers

 - **Content-Type**: application/json
 - **Accept**: application/json

[[Back to top]](#) [[Back to API list]](../README.md#documentation-for-api-endpoints) [[Back to Model list]](../README.md#documentation-for-models) [[Back to README]](../README.md)

# **unblock**
> Envelope unblock(userId)

Unblock a user

### Example
```dart
import 'package:vela_api_client/api.dart';

final api = VelaApiClient().getSafetyApi();
final String userId = 38400000-8cf0-11bd-b23e-10b96e4ef00d; // String | User id

try {
    final response = api.unblock(userId);
    print(response);
} catch on DioException (e) {
    print('Exception when calling SafetyApi->unblock: $e\n');
}
```

### Parameters

Name | Type | Description  | Notes
------------- | ------------- | ------------- | -------------
 **userId** | **String**| User id | 

### Return type

[**Envelope**](Envelope.md)

### Authorization

[bearerAuth](../README.md#bearerAuth)

### HTTP request headers

 - **Content-Type**: Not defined
 - **Accept**: application/json

[[Back to top]](#) [[Back to API list]](../README.md#documentation-for-api-endpoints) [[Back to Model list]](../README.md#documentation-for-models) [[Back to README]](../README.md)

