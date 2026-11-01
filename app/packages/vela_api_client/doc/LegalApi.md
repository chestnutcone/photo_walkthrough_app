# vela_api_client.api.LegalApi

## Load the API package
```dart
import 'package:vela_api_client/api.dart';
```

All URIs are relative to *http://localhost:5080*

Method | HTTP request | Description
------------- | ------------- | -------------
[**submitCounterNotice**](LegalApi.md#submitcounternotice) | **POST** /v1/works/{id}/counter-notice | Send a counter-notice for a story removed after a copyright notice
[**submitDmcaNotice**](LegalApi.md#submitdmcanotice) | **POST** /v1/legal/dmca-notices | Submit a copyright (DMCA) notice


# **submitCounterNotice**
> DmcaNoticeAckEnvelope submitCounterNotice(id, submitCounterNoticeRequest)

Send a counter-notice for a story removed after a copyright notice

Owner only, for a story that is currently removed after a takedown notice. Any other story (not yours, not removed, no takedown notice, unknown id) answers the same 404 work_not_found. One counter-notice per story (409 counter_notice_exists). Free text is stored verbatim; control characters other than new lines are rejected. The body may be at most 20 KB.

### Example
```dart
import 'package:vela_api_client/api.dart';

final api = VelaApiClient().getLegalApi();
final String id = 38400000-8cf0-11bd-b23e-10b96e4ef00d; // String | Resource id
final SubmitCounterNoticeRequest submitCounterNoticeRequest = ; // SubmitCounterNoticeRequest | 

try {
    final response = api.submitCounterNotice(id, submitCounterNoticeRequest);
    print(response);
} catch on DioException (e) {
    print('Exception when calling LegalApi->submitCounterNotice: $e\n');
}
```

### Parameters

Name | Type | Description  | Notes
------------- | ------------- | ------------- | -------------
 **id** | **String**| Resource id | 
 **submitCounterNoticeRequest** | [**SubmitCounterNoticeRequest**](SubmitCounterNoticeRequest.md)|  | 

### Return type

[**DmcaNoticeAckEnvelope**](DmcaNoticeAckEnvelope.md)

### Authorization

[bearerAuth](../README.md#bearerAuth)

### HTTP request headers

 - **Content-Type**: application/json
 - **Accept**: application/json

[[Back to top]](#) [[Back to API list]](../README.md#documentation-for-api-endpoints) [[Back to Model list]](../README.md#documentation-for-models) [[Back to README]](../README.md)

# **submitDmcaNotice**
> DmcaNoticeAckEnvelope submitDmcaNotice(submitDmcaNoticeRequest)

Submit a copyright (DMCA) notice

Public. Always answers 202 with a reference id, whether or not the reference matched a story (it never reveals which stories exist); unmatched notices are handled manually. Free text is stored verbatim; control characters other than new lines are rejected. Rate limited per client address (5 per rolling 24 hours, 429 rate_limited with Retry-After). The body may be at most 20 KB (413 payload_too_large). The claimant gets a plain-text acknowledgement email.

### Example
```dart
import 'package:vela_api_client/api.dart';

final api = VelaApiClient().getLegalApi();
final SubmitDmcaNoticeRequest submitDmcaNoticeRequest = ; // SubmitDmcaNoticeRequest | 

try {
    final response = api.submitDmcaNotice(submitDmcaNoticeRequest);
    print(response);
} catch on DioException (e) {
    print('Exception when calling LegalApi->submitDmcaNotice: $e\n');
}
```

### Parameters

Name | Type | Description  | Notes
------------- | ------------- | ------------- | -------------
 **submitDmcaNoticeRequest** | [**SubmitDmcaNoticeRequest**](SubmitDmcaNoticeRequest.md)|  | 

### Return type

[**DmcaNoticeAckEnvelope**](DmcaNoticeAckEnvelope.md)

### Authorization

No authorization required

### HTTP request headers

 - **Content-Type**: application/json
 - **Accept**: application/json

[[Back to top]](#) [[Back to API list]](../README.md#documentation-for-api-endpoints) [[Back to Model list]](../README.md#documentation-for-models) [[Back to README]](../README.md)

