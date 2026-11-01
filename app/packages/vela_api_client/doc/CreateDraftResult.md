# vela_api_client.model.CreateDraftResult

## Load the model package
```dart
import 'package:vela_api_client/api.dart';
```

## Properties
Name | Type | Description | Notes
------------ | ------------- | ------------- | -------------
**workId** | **String** |  | 
**uploadUrl** | **String** | Write-only signed URL for the original image (PUT with x-ms-blob-type: BlockBlob), valid for at most 15 minutes | 
**expiresAt** | [**DateTime**](DateTime.md) | When the upload URL stops working (the draft itself lives 24 hours) | 

[[Back to Model list]](../README.md#documentation-for-models) [[Back to API list]](../README.md#documentation-for-api-endpoints) [[Back to README]](../README.md)


