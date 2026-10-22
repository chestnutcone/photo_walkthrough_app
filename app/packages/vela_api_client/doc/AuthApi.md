# vela_api_client.api.AuthApi

## Load the API package
```dart
import 'package:vela_api_client/api.dart';
```

All URIs are relative to *http://localhost:5080*

Method | HTTP request | Description
------------- | ------------- | -------------
[**emailRequest**](AuthApi.md#emailrequest) | **POST** /v1/auth/email/request | Request an email one-time code
[**emailVerify**](AuthApi.md#emailverify) | **POST** /v1/auth/email/verify | Verify an email one-time code
[**logout**](AuthApi.md#logout) | **POST** /v1/auth/logout | Revoke a refresh token
[**refresh**](AuthApi.md#refresh) | **POST** /v1/auth/refresh | Rotate a refresh token
[**ssoSignIn**](AuthApi.md#ssosignin) | **POST** /v1/auth/sso | Sign in with Apple or Google


# **emailRequest**
> Envelope emailRequest(emailRequestRequest)

Request an email one-time code

### Example
```dart
import 'package:vela_api_client/api.dart';

final api = VelaApiClient().getAuthApi();
final EmailRequestRequest emailRequestRequest = ; // EmailRequestRequest | 

try {
    final response = api.emailRequest(emailRequestRequest);
    print(response);
} catch on DioException (e) {
    print('Exception when calling AuthApi->emailRequest: $e\n');
}
```

### Parameters

Name | Type | Description  | Notes
------------- | ------------- | ------------- | -------------
 **emailRequestRequest** | [**EmailRequestRequest**](EmailRequestRequest.md)|  | 

### Return type

[**Envelope**](Envelope.md)

### Authorization

No authorization required

### HTTP request headers

 - **Content-Type**: application/json
 - **Accept**: application/json

[[Back to top]](#) [[Back to API list]](../README.md#documentation-for-api-endpoints) [[Back to Model list]](../README.md#documentation-for-models) [[Back to README]](../README.md)

# **emailVerify**
> TokenPairEnvelope emailVerify(emailVerifyRequest)

Verify an email one-time code

### Example
```dart
import 'package:vela_api_client/api.dart';

final api = VelaApiClient().getAuthApi();
final EmailVerifyRequest emailVerifyRequest = ; // EmailVerifyRequest | 

try {
    final response = api.emailVerify(emailVerifyRequest);
    print(response);
} catch on DioException (e) {
    print('Exception when calling AuthApi->emailVerify: $e\n');
}
```

### Parameters

Name | Type | Description  | Notes
------------- | ------------- | ------------- | -------------
 **emailVerifyRequest** | [**EmailVerifyRequest**](EmailVerifyRequest.md)|  | 

### Return type

[**TokenPairEnvelope**](TokenPairEnvelope.md)

### Authorization

No authorization required

### HTTP request headers

 - **Content-Type**: application/json
 - **Accept**: application/json

[[Back to top]](#) [[Back to API list]](../README.md#documentation-for-api-endpoints) [[Back to Model list]](../README.md#documentation-for-models) [[Back to README]](../README.md)

# **logout**
> Envelope logout(logoutRequest)

Revoke a refresh token

### Example
```dart
import 'package:vela_api_client/api.dart';

final api = VelaApiClient().getAuthApi();
final LogoutRequest logoutRequest = ; // LogoutRequest | 

try {
    final response = api.logout(logoutRequest);
    print(response);
} catch on DioException (e) {
    print('Exception when calling AuthApi->logout: $e\n');
}
```

### Parameters

Name | Type | Description  | Notes
------------- | ------------- | ------------- | -------------
 **logoutRequest** | [**LogoutRequest**](LogoutRequest.md)|  | 

### Return type

[**Envelope**](Envelope.md)

### Authorization

No authorization required

### HTTP request headers

 - **Content-Type**: application/json
 - **Accept**: application/json

[[Back to top]](#) [[Back to API list]](../README.md#documentation-for-api-endpoints) [[Back to Model list]](../README.md#documentation-for-models) [[Back to README]](../README.md)

# **refresh**
> TokenPairEnvelope refresh(refreshRequest)

Rotate a refresh token

### Example
```dart
import 'package:vela_api_client/api.dart';

final api = VelaApiClient().getAuthApi();
final RefreshRequest refreshRequest = ; // RefreshRequest | 

try {
    final response = api.refresh(refreshRequest);
    print(response);
} catch on DioException (e) {
    print('Exception when calling AuthApi->refresh: $e\n');
}
```

### Parameters

Name | Type | Description  | Notes
------------- | ------------- | ------------- | -------------
 **refreshRequest** | [**RefreshRequest**](RefreshRequest.md)|  | 

### Return type

[**TokenPairEnvelope**](TokenPairEnvelope.md)

### Authorization

No authorization required

### HTTP request headers

 - **Content-Type**: application/json
 - **Accept**: application/json

[[Back to top]](#) [[Back to API list]](../README.md#documentation-for-api-endpoints) [[Back to Model list]](../README.md#documentation-for-models) [[Back to README]](../README.md)

# **ssoSignIn**
> TokenPairEnvelope ssoSignIn(ssoSignInRequest)

Sign in with Apple or Google

### Example
```dart
import 'package:vela_api_client/api.dart';

final api = VelaApiClient().getAuthApi();
final SsoSignInRequest ssoSignInRequest = ; // SsoSignInRequest | 

try {
    final response = api.ssoSignIn(ssoSignInRequest);
    print(response);
} catch on DioException (e) {
    print('Exception when calling AuthApi->ssoSignIn: $e\n');
}
```

### Parameters

Name | Type | Description  | Notes
------------- | ------------- | ------------- | -------------
 **ssoSignInRequest** | [**SsoSignInRequest**](SsoSignInRequest.md)|  | 

### Return type

[**TokenPairEnvelope**](TokenPairEnvelope.md)

### Authorization

No authorization required

### HTTP request headers

 - **Content-Type**: application/json
 - **Accept**: application/json

[[Back to top]](#) [[Back to API list]](../README.md#documentation-for-api-endpoints) [[Back to Model list]](../README.md#documentation-for-models) [[Back to README]](../README.md)

