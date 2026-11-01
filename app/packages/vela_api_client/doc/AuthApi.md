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
> Envelope emailRequest(emailRequestRequest, xAttestation, xPlatform)

Request an email one-time code

Errors: 400 invalid_email; 400 disposable_email for throwaway-mailbox domains; 403 attestation_required / attestation_failed when the server enforces app attestation; 429 rate_limited (5 per email and 20 per IP per hour).

### Example
```dart
import 'package:vela_api_client/api.dart';

final api = VelaApiClient().getAuthApi();
final EmailRequestRequest emailRequestRequest = ; // EmailRequestRequest | 
final String xAttestation = xAttestation_example; // String | App attestation token (Play Integrity on Android, App Attest on iOS), minted for this operation. Required when the server enforces attestation; failing it is 403 attestation_required or attestation_failed.
final String xPlatform = xPlatform_example; // String | Platform that minted X-Attestation

try {
    final response = api.emailRequest(emailRequestRequest, xAttestation, xPlatform);
    print(response);
} catch on DioException (e) {
    print('Exception when calling AuthApi->emailRequest: $e\n');
}
```

### Parameters

Name | Type | Description  | Notes
------------- | ------------- | ------------- | -------------
 **emailRequestRequest** | [**EmailRequestRequest**](EmailRequestRequest.md)|  | 
 **xAttestation** | **String**| App attestation token (Play Integrity on Android, App Attest on iOS), minted for this operation. Required when the server enforces attestation; failing it is 403 attestation_required or attestation_failed. | [optional] 
 **xPlatform** | **String**| Platform that minted X-Attestation | [optional] 

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
> TokenPairEnvelope ssoSignIn(ssoSignInRequest, xAttestation, xPlatform)

Sign in with Apple or Google

### Example
```dart
import 'package:vela_api_client/api.dart';

final api = VelaApiClient().getAuthApi();
final SsoSignInRequest ssoSignInRequest = ; // SsoSignInRequest | 
final String xAttestation = xAttestation_example; // String | App attestation token (Play Integrity on Android, App Attest on iOS), minted for this operation. Required when the server enforces attestation; failing it is 403 attestation_required or attestation_failed.
final String xPlatform = xPlatform_example; // String | Platform that minted X-Attestation

try {
    final response = api.ssoSignIn(ssoSignInRequest, xAttestation, xPlatform);
    print(response);
} catch on DioException (e) {
    print('Exception when calling AuthApi->ssoSignIn: $e\n');
}
```

### Parameters

Name | Type | Description  | Notes
------------- | ------------- | ------------- | -------------
 **ssoSignInRequest** | [**SsoSignInRequest**](SsoSignInRequest.md)|  | 
 **xAttestation** | **String**| App attestation token (Play Integrity on Android, App Attest on iOS), minted for this operation. Required when the server enforces attestation; failing it is 403 attestation_required or attestation_failed. | [optional] 
 **xPlatform** | **String**| Platform that minted X-Attestation | [optional] 

### Return type

[**TokenPairEnvelope**](TokenPairEnvelope.md)

### Authorization

No authorization required

### HTTP request headers

 - **Content-Type**: application/json
 - **Accept**: application/json

[[Back to top]](#) [[Back to API list]](../README.md#documentation-for-api-endpoints) [[Back to Model list]](../README.md#documentation-for-models) [[Back to README]](../README.md)

