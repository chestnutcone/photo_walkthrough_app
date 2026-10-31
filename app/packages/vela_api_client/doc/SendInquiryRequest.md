# vela_api_client.model.SendInquiryRequest

## Load the model package
```dart
import 'package:vela_api_client/api.dart';
```

## Properties
Name | Type | Description | Notes
------------ | ------------- | ------------- | -------------
**type** | **String** |  | 
**email** | **String** | Sender's reply email | 
**message** | **String** | Cleaned by the shared text sanitizer (multi-line), then 1 to 2000 code points; bidirectional override characters are refused | 
**workId** | **String** |  | [optional] 

[[Back to Model list]](../README.md#documentation-for-models) [[Back to API list]](../README.md#documentation-for-api-endpoints) [[Back to README]](../README.md)


