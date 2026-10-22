# vela_api_client.api.ArtistsApi

## Load the API package
```dart
import 'package:vela_api_client/api.dart';
```

All URIs are relative to *http://localhost:5080*

Method | HTTP request | Description
------------- | ------------- | -------------
[**follow**](ArtistsApi.md#follow) | **PUT** /v1/artists/{id}/follow | Follow an artist
[**getArtist**](ArtistsApi.md#getartist) | **GET** /v1/artists/{handle} | Get an artist by handle
[**listArtistWorks**](ArtistsApi.md#listartistworks) | **GET** /v1/artists/{handle}/works | List an artist&#39;s published works
[**listFollowers**](ArtistsApi.md#listfollowers) | **GET** /v1/artists/{id}/followers | List an artist&#39;s followers
[**sendInquiry**](ArtistsApi.md#sendinquiry) | **POST** /v1/artists/{id}/inquiries | Send an inquiry to an artist
[**suggestedArtists**](ArtistsApi.md#suggestedartists) | **GET** /v1/artists/suggested | Suggested artists to follow
[**unfollow**](ArtistsApi.md#unfollow) | **DELETE** /v1/artists/{id}/follow | Unfollow an artist


# **follow**
> Envelope follow(id)

Follow an artist

### Example
```dart
import 'package:vela_api_client/api.dart';

final api = VelaApiClient().getArtistsApi();
final String id = 38400000-8cf0-11bd-b23e-10b96e4ef00d; // String | Resource id

try {
    final response = api.follow(id);
    print(response);
} catch on DioException (e) {
    print('Exception when calling ArtistsApi->follow: $e\n');
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

# **getArtist**
> ProfileEnvelope getArtist(handle)

Get an artist by handle

### Example
```dart
import 'package:vela_api_client/api.dart';

final api = VelaApiClient().getArtistsApi();
final String handle = handle_example; // String | Artist handle (lowercase)

try {
    final response = api.getArtist(handle);
    print(response);
} catch on DioException (e) {
    print('Exception when calling ArtistsApi->getArtist: $e\n');
}
```

### Parameters

Name | Type | Description  | Notes
------------- | ------------- | ------------- | -------------
 **handle** | **String**| Artist handle (lowercase) | 

### Return type

[**ProfileEnvelope**](ProfileEnvelope.md)

### Authorization

No authorization required

### HTTP request headers

 - **Content-Type**: Not defined
 - **Accept**: application/json

[[Back to top]](#) [[Back to API list]](../README.md#documentation-for-api-endpoints) [[Back to Model list]](../README.md#documentation-for-models) [[Back to README]](../README.md)

# **listArtistWorks**
> WorkListEnvelope listArtistWorks(handle, cursor, limit)

List an artist's published works

### Example
```dart
import 'package:vela_api_client/api.dart';

final api = VelaApiClient().getArtistsApi();
final String handle = handle_example; // String | Artist handle (lowercase)
final String cursor = cursor_example; // String | Opaque cursor from the previous page
final int limit = 56; // int | Page size, 1 to 50, default 20

try {
    final response = api.listArtistWorks(handle, cursor, limit);
    print(response);
} catch on DioException (e) {
    print('Exception when calling ArtistsApi->listArtistWorks: $e\n');
}
```

### Parameters

Name | Type | Description  | Notes
------------- | ------------- | ------------- | -------------
 **handle** | **String**| Artist handle (lowercase) | 
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

# **listFollowers**
> ProfileListEnvelope listFollowers(id, cursor, limit)

List an artist's followers

### Example
```dart
import 'package:vela_api_client/api.dart';

final api = VelaApiClient().getArtistsApi();
final String id = 38400000-8cf0-11bd-b23e-10b96e4ef00d; // String | Resource id
final String cursor = cursor_example; // String | Opaque cursor from the previous page
final int limit = 56; // int | Page size, 1 to 50, default 20

try {
    final response = api.listFollowers(id, cursor, limit);
    print(response);
} catch on DioException (e) {
    print('Exception when calling ArtistsApi->listFollowers: $e\n');
}
```

### Parameters

Name | Type | Description  | Notes
------------- | ------------- | ------------- | -------------
 **id** | **String**| Resource id | 
 **cursor** | **String**| Opaque cursor from the previous page | [optional] 
 **limit** | **int**| Page size, 1 to 50, default 20 | [optional] [default to 20]

### Return type

[**ProfileListEnvelope**](ProfileListEnvelope.md)

### Authorization

[bearerAuth](../README.md#bearerAuth)

### HTTP request headers

 - **Content-Type**: Not defined
 - **Accept**: application/json

[[Back to top]](#) [[Back to API list]](../README.md#documentation-for-api-endpoints) [[Back to Model list]](../README.md#documentation-for-models) [[Back to README]](../README.md)

# **sendInquiry**
> InquiryEnvelope sendInquiry(id, sendInquiryRequest)

Send an inquiry to an artist

### Example
```dart
import 'package:vela_api_client/api.dart';

final api = VelaApiClient().getArtistsApi();
final String id = 38400000-8cf0-11bd-b23e-10b96e4ef00d; // String | Resource id
final SendInquiryRequest sendInquiryRequest = ; // SendInquiryRequest | 

try {
    final response = api.sendInquiry(id, sendInquiryRequest);
    print(response);
} catch on DioException (e) {
    print('Exception when calling ArtistsApi->sendInquiry: $e\n');
}
```

### Parameters

Name | Type | Description  | Notes
------------- | ------------- | ------------- | -------------
 **id** | **String**| Resource id | 
 **sendInquiryRequest** | [**SendInquiryRequest**](SendInquiryRequest.md)|  | 

### Return type

[**InquiryEnvelope**](InquiryEnvelope.md)

### Authorization

[bearerAuth](../README.md#bearerAuth)

### HTTP request headers

 - **Content-Type**: application/json
 - **Accept**: application/json

[[Back to top]](#) [[Back to API list]](../README.md#documentation-for-api-endpoints) [[Back to Model list]](../README.md#documentation-for-models) [[Back to README]](../README.md)

# **suggestedArtists**
> ProfileListEnvelope suggestedArtists(cursor, limit)

Suggested artists to follow

### Example
```dart
import 'package:vela_api_client/api.dart';

final api = VelaApiClient().getArtistsApi();
final String cursor = cursor_example; // String | Opaque cursor from the previous page
final int limit = 56; // int | Page size, 1 to 50, default 20

try {
    final response = api.suggestedArtists(cursor, limit);
    print(response);
} catch on DioException (e) {
    print('Exception when calling ArtistsApi->suggestedArtists: $e\n');
}
```

### Parameters

Name | Type | Description  | Notes
------------- | ------------- | ------------- | -------------
 **cursor** | **String**| Opaque cursor from the previous page | [optional] 
 **limit** | **int**| Page size, 1 to 50, default 20 | [optional] [default to 20]

### Return type

[**ProfileListEnvelope**](ProfileListEnvelope.md)

### Authorization

No authorization required

### HTTP request headers

 - **Content-Type**: Not defined
 - **Accept**: application/json

[[Back to top]](#) [[Back to API list]](../README.md#documentation-for-api-endpoints) [[Back to Model list]](../README.md#documentation-for-models) [[Back to README]](../README.md)

# **unfollow**
> Envelope unfollow(id)

Unfollow an artist

### Example
```dart
import 'package:vela_api_client/api.dart';

final api = VelaApiClient().getArtistsApi();
final String id = 38400000-8cf0-11bd-b23e-10b96e4ef00d; // String | Resource id

try {
    final response = api.unfollow(id);
    print(response);
} catch on DioException (e) {
    print('Exception when calling ArtistsApi->unfollow: $e\n');
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

