# vela_api_client.model.Work

## Load the model package
```dart
import 'package:vela_api_client/api.dart';
```

## Properties
Name | Type | Description | Notes
------------ | ------------- | ------------- | -------------
**id** | **String** |  | 
**author** | [**ArtistSummary**](ArtistSummary.md) |  | 
**title** | **String** |  | 
**intro** | **String** |  | [optional] 
**altText** | **String** |  | [optional] 
**isOwner** | **bool** |  | 
**status** | **String** | Present only for the owner and admins. | [optional] 
**imageUrl** | **String** | Full-size variant | [optional] 
**thumbUrl** | **String** |  | [optional] 
**feedUrl** | **String** |  | [optional] 
**imageWidth** | **int** |  | [optional] 
**imageHeight** | **int** |  | [optional] 
**chapters** | [**List&lt;Chapter&gt;**](Chapter.md) |  | 
**createdAt** | [**DateTime**](DateTime.md) |  | 
**publishedAt** | [**DateTime**](DateTime.md) |  | [optional] 
**draftExpiresAt** | [**DateTime**](DateTime.md) |  | [optional] 
**shareUrl** | **String** |  | 

[[Back to Model list]](../README.md#documentation-for-models) [[Back to API list]](../README.md#documentation-for-api-endpoints) [[Back to README]](../README.md)


