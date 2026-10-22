# vela_api_client.api.NotificationsApi

## Load the API package
```dart
import 'package:vela_api_client/api.dart';
```

All URIs are relative to *http://localhost:5080*

Method | HTTP request | Description
------------- | ------------- | -------------
[**listNotifications**](NotificationsApi.md#listnotifications) | **GET** /v1/notifications | List notifications
[**markRead**](NotificationsApi.md#markread) | **POST** /v1/notifications/read | Mark notifications read
[**unreadCount**](NotificationsApi.md#unreadcount) | **GET** /v1/notifications/unread-count | Count unread notifications


# **listNotifications**
> NotificationListEnvelope listNotifications(cursor, limit)

List notifications

### Example
```dart
import 'package:vela_api_client/api.dart';

final api = VelaApiClient().getNotificationsApi();
final String cursor = cursor_example; // String | Opaque cursor from the previous page
final int limit = 56; // int | Page size, 1 to 50, default 20

try {
    final response = api.listNotifications(cursor, limit);
    print(response);
} catch on DioException (e) {
    print('Exception when calling NotificationsApi->listNotifications: $e\n');
}
```

### Parameters

Name | Type | Description  | Notes
------------- | ------------- | ------------- | -------------
 **cursor** | **String**| Opaque cursor from the previous page | [optional] 
 **limit** | **int**| Page size, 1 to 50, default 20 | [optional] [default to 20]

### Return type

[**NotificationListEnvelope**](NotificationListEnvelope.md)

### Authorization

[bearerAuth](../README.md#bearerAuth)

### HTTP request headers

 - **Content-Type**: Not defined
 - **Accept**: application/json

[[Back to top]](#) [[Back to API list]](../README.md#documentation-for-api-endpoints) [[Back to Model list]](../README.md#documentation-for-models) [[Back to README]](../README.md)

# **markRead**
> Envelope markRead(markReadRequest)

Mark notifications read

### Example
```dart
import 'package:vela_api_client/api.dart';

final api = VelaApiClient().getNotificationsApi();
final MarkReadRequest markReadRequest = ; // MarkReadRequest | 

try {
    final response = api.markRead(markReadRequest);
    print(response);
} catch on DioException (e) {
    print('Exception when calling NotificationsApi->markRead: $e\n');
}
```

### Parameters

Name | Type | Description  | Notes
------------- | ------------- | ------------- | -------------
 **markReadRequest** | [**MarkReadRequest**](MarkReadRequest.md)|  | 

### Return type

[**Envelope**](Envelope.md)

### Authorization

[bearerAuth](../README.md#bearerAuth)

### HTTP request headers

 - **Content-Type**: application/json
 - **Accept**: application/json

[[Back to top]](#) [[Back to API list]](../README.md#documentation-for-api-endpoints) [[Back to Model list]](../README.md#documentation-for-models) [[Back to README]](../README.md)

# **unreadCount**
> UnreadCountEnvelope unreadCount()

Count unread notifications

### Example
```dart
import 'package:vela_api_client/api.dart';

final api = VelaApiClient().getNotificationsApi();

try {
    final response = api.unreadCount();
    print(response);
} catch on DioException (e) {
    print('Exception when calling NotificationsApi->unreadCount: $e\n');
}
```

### Parameters
This endpoint does not need any parameter.

### Return type

[**UnreadCountEnvelope**](UnreadCountEnvelope.md)

### Authorization

[bearerAuth](../README.md#bearerAuth)

### HTTP request headers

 - **Content-Type**: Not defined
 - **Accept**: application/json

[[Back to top]](#) [[Back to API list]](../README.md#documentation-for-api-endpoints) [[Back to Model list]](../README.md#documentation-for-models) [[Back to README]](../README.md)

