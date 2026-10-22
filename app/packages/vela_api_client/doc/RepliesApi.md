# vela_api_client.api.RepliesApi

## Load the API package
```dart
import 'package:vela_api_client/api.dart';
```

All URIs are relative to *http://localhost:5080*

Method | HTTP request | Description
------------- | ------------- | -------------
[**addReply**](RepliesApi.md#addreply) | **POST** /v1/works/{id}/chapters/{index}/replies | Reply to a chapter
[**deleteReply**](RepliesApi.md#deletereply) | **DELETE** /v1/replies/{id} | Delete a reply
[**listReplies**](RepliesApi.md#listreplies) | **GET** /v1/works/{id}/chapters/{index}/replies | List replies on a chapter


# **addReply**
> ReplyEnvelope addReply(id, index, addReplyRequest)

Reply to a chapter

Body is trimmed, 1 to 1000 characters; control characters other than newline are refused. A reply the safety scan flags is still returned to its author, with hiddenForOthers true.

### Example
```dart
import 'package:vela_api_client/api.dart';

final api = VelaApiClient().getRepliesApi();
final String id = 38400000-8cf0-11bd-b23e-10b96e4ef00d; // String | Resource id
final int index = 56; // int | Zero-based chapter index
final AddReplyRequest addReplyRequest = ; // AddReplyRequest | 

try {
    final response = api.addReply(id, index, addReplyRequest);
    print(response);
} catch on DioException (e) {
    print('Exception when calling RepliesApi->addReply: $e\n');
}
```

### Parameters

Name | Type | Description  | Notes
------------- | ------------- | ------------- | -------------
 **id** | **String**| Resource id | 
 **index** | **int**| Zero-based chapter index | 
 **addReplyRequest** | [**AddReplyRequest**](AddReplyRequest.md)|  | 

### Return type

[**ReplyEnvelope**](ReplyEnvelope.md)

### Authorization

[bearerAuth](../README.md#bearerAuth)

### HTTP request headers

 - **Content-Type**: application/json
 - **Accept**: application/json

[[Back to top]](#) [[Back to API list]](../README.md#documentation-for-api-endpoints) [[Back to Model list]](../README.md#documentation-for-models) [[Back to README]](../README.md)

# **deleteReply**
> Envelope deleteReply(id)

Delete a reply

### Example
```dart
import 'package:vela_api_client/api.dart';

final api = VelaApiClient().getRepliesApi();
final String id = 38400000-8cf0-11bd-b23e-10b96e4ef00d; // String | Resource id

try {
    final response = api.deleteReply(id);
    print(response);
} catch on DioException (e) {
    print('Exception when calling RepliesApi->deleteReply: $e\n');
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

# **listReplies**
> ReplyListEnvelope listReplies(id, index, cursor, limit)

List replies on a chapter

### Example
```dart
import 'package:vela_api_client/api.dart';

final api = VelaApiClient().getRepliesApi();
final String id = 38400000-8cf0-11bd-b23e-10b96e4ef00d; // String | Resource id
final int index = 56; // int | Zero-based chapter index
final String cursor = cursor_example; // String | Opaque cursor from the previous page
final int limit = 56; // int | Page size, 1 to 50, default 20

try {
    final response = api.listReplies(id, index, cursor, limit);
    print(response);
} catch on DioException (e) {
    print('Exception when calling RepliesApi->listReplies: $e\n');
}
```

### Parameters

Name | Type | Description  | Notes
------------- | ------------- | ------------- | -------------
 **id** | **String**| Resource id | 
 **index** | **int**| Zero-based chapter index | 
 **cursor** | **String**| Opaque cursor from the previous page | [optional] 
 **limit** | **int**| Page size, 1 to 50, default 20 | [optional] [default to 20]

### Return type

[**ReplyListEnvelope**](ReplyListEnvelope.md)

### Authorization

No authorization required

### HTTP request headers

 - **Content-Type**: Not defined
 - **Accept**: application/json

[[Back to top]](#) [[Back to API list]](../README.md#documentation-for-api-endpoints) [[Back to Model list]](../README.md#documentation-for-models) [[Back to README]](../README.md)

