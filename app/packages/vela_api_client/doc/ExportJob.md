# vela_api_client.model.ExportJob

## Load the model package
```dart
import 'package:vela_api_client/api.dart';
```

## Properties
Name | Type | Description | Notes
------------ | ------------- | ------------- | -------------
**id** | **String** |  | 
**status** | **String** | expired = past its 7-day life; request a new export | 
**downloadUrl** | **String** | Read-only signed URL for the zip, valid for at most 15 minutes; present only while status is ready (fetch the export again for a fresh one) | [optional] 
**createdAt** | [**DateTime**](DateTime.md) |  | 
**expiresAt** | [**DateTime**](DateTime.md) |  | [optional] 

[[Back to Model list]](../README.md#documentation-for-models) [[Back to API list]](../README.md#documentation-for-api-endpoints) [[Back to README]](../README.md)


