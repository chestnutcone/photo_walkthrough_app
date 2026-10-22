# vela_api_client.model.Notification

## Load the model package
```dart
import 'package:vela_api_client/api.dart';
```

## Properties
Name | Type | Description | Notes
------------ | ------------- | ------------- | -------------
**id** | **String** |  | 
**type** | **String** |  | 
**actor** | [**Profile**](Profile.md) |  | 
**workId** | **String** |  | [optional] 
**workTitle** | **String** | Title of the story the notification is about | [optional] 
**chapterIndex** | **int** |  | [optional] 
**replyId** | **String** |  | [optional] 
**createdAt** | [**DateTime**](DateTime.md) |  | 
**read** | **bool** |  | 
**count** | **int** | Rows folded into this item. Consecutive unread replies on one chapter collapse into one item (render \"3 new replies on <title>\"); `id` and `actor` are the latest row's. 1 for every other item. | [optional] 
**ids** | **List&lt;String&gt;** | Ids of every row folded into this item (pass them to markRead) | [optional] 

[[Back to Model list]](../README.md#documentation-for-models) [[Back to API list]](../README.md#documentation-for-api-endpoints) [[Back to README]](../README.md)


