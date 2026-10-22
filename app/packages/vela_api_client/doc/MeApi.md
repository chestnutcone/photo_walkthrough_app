# vela_api_client.api.MeApi

## Load the API package
```dart
import 'package:vela_api_client/api.dart';
```

All URIs are relative to *http://localhost:5080*

Method | HTTP request | Description
------------- | ------------- | -------------
[**avatarUpload**](MeApi.md#avatarupload) | **POST** /v1/me/avatar-upload | Get a signed avatar upload URL
[**completeOnboarding**](MeApi.md#completeonboarding) | **POST** /v1/me/onboarding | Finish onboarding
[**deleteAccount**](MeApi.md#deleteaccount) | **DELETE** /v1/me | Delete the account and all data
[**getExport**](MeApi.md#getexport) | **GET** /v1/me/export/{id} | Get a data export
[**getMe**](MeApi.md#getme) | **GET** /v1/me | Get the signed-in profile
[**requestExport**](MeApi.md#requestexport) | **POST** /v1/me/export | Request a data export
[**updateProfile**](MeApi.md#updateprofile) | **PATCH** /v1/me/profile | Update profile fields


# **avatarUpload**
> UploadTicketEnvelope avatarUpload()

Get a signed avatar upload URL

### Example
```dart
import 'package:vela_api_client/api.dart';

final api = VelaApiClient().getMeApi();

try {
    final response = api.avatarUpload();
    print(response);
} catch on DioException (e) {
    print('Exception when calling MeApi->avatarUpload: $e\n');
}
```

### Parameters
This endpoint does not need any parameter.

### Return type

[**UploadTicketEnvelope**](UploadTicketEnvelope.md)

### Authorization

[bearerAuth](../README.md#bearerAuth)

### HTTP request headers

 - **Content-Type**: Not defined
 - **Accept**: application/json

[[Back to top]](#) [[Back to API list]](../README.md#documentation-for-api-endpoints) [[Back to Model list]](../README.md#documentation-for-models) [[Back to README]](../README.md)

# **completeOnboarding**
> MeEnvelope completeOnboarding(completeOnboardingRequest)

Finish onboarding

### Example
```dart
import 'package:vela_api_client/api.dart';

final api = VelaApiClient().getMeApi();
final CompleteOnboardingRequest completeOnboardingRequest = ; // CompleteOnboardingRequest | 

try {
    final response = api.completeOnboarding(completeOnboardingRequest);
    print(response);
} catch on DioException (e) {
    print('Exception when calling MeApi->completeOnboarding: $e\n');
}
```

### Parameters

Name | Type | Description  | Notes
------------- | ------------- | ------------- | -------------
 **completeOnboardingRequest** | [**CompleteOnboardingRequest**](CompleteOnboardingRequest.md)|  | 

### Return type

[**MeEnvelope**](MeEnvelope.md)

### Authorization

[bearerAuth](../README.md#bearerAuth)

### HTTP request headers

 - **Content-Type**: application/json
 - **Accept**: application/json

[[Back to top]](#) [[Back to API list]](../README.md#documentation-for-api-endpoints) [[Back to Model list]](../README.md#documentation-for-models) [[Back to README]](../README.md)

# **deleteAccount**
> Envelope deleteAccount()

Delete the account and all data

### Example
```dart
import 'package:vela_api_client/api.dart';

final api = VelaApiClient().getMeApi();

try {
    final response = api.deleteAccount();
    print(response);
} catch on DioException (e) {
    print('Exception when calling MeApi->deleteAccount: $e\n');
}
```

### Parameters
This endpoint does not need any parameter.

### Return type

[**Envelope**](Envelope.md)

### Authorization

[bearerAuth](../README.md#bearerAuth)

### HTTP request headers

 - **Content-Type**: Not defined
 - **Accept**: application/json

[[Back to top]](#) [[Back to API list]](../README.md#documentation-for-api-endpoints) [[Back to Model list]](../README.md#documentation-for-models) [[Back to README]](../README.md)

# **getExport**
> ExportJobEnvelope getExport(id)

Get a data export

### Example
```dart
import 'package:vela_api_client/api.dart';

final api = VelaApiClient().getMeApi();
final String id = 38400000-8cf0-11bd-b23e-10b96e4ef00d; // String | Resource id

try {
    final response = api.getExport(id);
    print(response);
} catch on DioException (e) {
    print('Exception when calling MeApi->getExport: $e\n');
}
```

### Parameters

Name | Type | Description  | Notes
------------- | ------------- | ------------- | -------------
 **id** | **String**| Resource id | 

### Return type

[**ExportJobEnvelope**](ExportJobEnvelope.md)

### Authorization

[bearerAuth](../README.md#bearerAuth)

### HTTP request headers

 - **Content-Type**: Not defined
 - **Accept**: application/json

[[Back to top]](#) [[Back to API list]](../README.md#documentation-for-api-endpoints) [[Back to Model list]](../README.md#documentation-for-models) [[Back to README]](../README.md)

# **getMe**
> MeEnvelope getMe()

Get the signed-in profile

### Example
```dart
import 'package:vela_api_client/api.dart';

final api = VelaApiClient().getMeApi();

try {
    final response = api.getMe();
    print(response);
} catch on DioException (e) {
    print('Exception when calling MeApi->getMe: $e\n');
}
```

### Parameters
This endpoint does not need any parameter.

### Return type

[**MeEnvelope**](MeEnvelope.md)

### Authorization

[bearerAuth](../README.md#bearerAuth)

### HTTP request headers

 - **Content-Type**: Not defined
 - **Accept**: application/json

[[Back to top]](#) [[Back to API list]](../README.md#documentation-for-api-endpoints) [[Back to Model list]](../README.md#documentation-for-models) [[Back to README]](../README.md)

# **requestExport**
> ExportJobEnvelope requestExport()

Request a data export

### Example
```dart
import 'package:vela_api_client/api.dart';

final api = VelaApiClient().getMeApi();

try {
    final response = api.requestExport();
    print(response);
} catch on DioException (e) {
    print('Exception when calling MeApi->requestExport: $e\n');
}
```

### Parameters
This endpoint does not need any parameter.

### Return type

[**ExportJobEnvelope**](ExportJobEnvelope.md)

### Authorization

[bearerAuth](../README.md#bearerAuth)

### HTTP request headers

 - **Content-Type**: Not defined
 - **Accept**: application/json

[[Back to top]](#) [[Back to API list]](../README.md#documentation-for-api-endpoints) [[Back to Model list]](../README.md#documentation-for-models) [[Back to README]](../README.md)

# **updateProfile**
> MeEnvelope updateProfile(updateProfileRequest)

Update profile fields

Send avatarUploaded: true after uploading to the avatar upload URL; the server builds the 256 px avatar. 400 invalid_image when the upload is missing, not a JPEG/PNG/WebP, over 25 MB or over 60 megapixels.

### Example
```dart
import 'package:vela_api_client/api.dart';

final api = VelaApiClient().getMeApi();
final UpdateProfileRequest updateProfileRequest = ; // UpdateProfileRequest | 

try {
    final response = api.updateProfile(updateProfileRequest);
    print(response);
} catch on DioException (e) {
    print('Exception when calling MeApi->updateProfile: $e\n');
}
```

### Parameters

Name | Type | Description  | Notes
------------- | ------------- | ------------- | -------------
 **updateProfileRequest** | [**UpdateProfileRequest**](UpdateProfileRequest.md)|  | 

### Return type

[**MeEnvelope**](MeEnvelope.md)

### Authorization

[bearerAuth](../README.md#bearerAuth)

### HTTP request headers

 - **Content-Type**: application/json
 - **Accept**: application/json

[[Back to top]](#) [[Back to API list]](../README.md#documentation-for-api-endpoints) [[Back to Model list]](../README.md#documentation-for-models) [[Back to README]](../README.md)

