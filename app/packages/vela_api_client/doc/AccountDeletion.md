# vela_api_client.model.AccountDeletion

## Load the model package
```dart
import 'package:vela_api_client/api.dart';
```

## Properties
Name | Type | Description | Notes
------------ | ------------- | ------------- | -------------
**state** | **String** | deferred_review = content is under moderation review; the hard delete waits for it | 
**completesBy** | [**DateTime**](DateTime.md) | For deferred_review: the latest moment the hard delete runs (30 days after the request); earlier once the reviews end. Null when scheduled | 

[[Back to Model list]](../README.md#documentation-for-models) [[Back to API list]](../README.md#documentation-for-api-endpoints) [[Back to README]](../README.md)


