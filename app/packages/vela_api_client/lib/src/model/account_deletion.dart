//
// AUTO-GENERATED FILE, DO NOT MODIFY!
//

// ignore_for_file: unused_element
import 'package:json_annotation/json_annotation.dart';

part 'account_deletion.g.dart';


@JsonSerializable(
  checked: true,
  createToJson: true,
  disallowUnrecognizedKeys: false,
  explicitToJson: true,
)
class AccountDeletion {
  /// Returns a new [AccountDeletion] instance.
  AccountDeletion({

    required  this.state,

    required  this.completesBy,
  });

      /// deferred_review = content is under moderation review; the hard delete waits for it
  @JsonKey(
    
    name: r'state',
    required: true,
    includeIfNull: false,
  )


  final AccountDeletionStateEnum state;



      /// For deferred_review: the latest moment the hard delete runs (30 days after the request); earlier once the reviews end. Null when scheduled
  @JsonKey(
    
    name: r'completesBy',
    required: true,
    includeIfNull: true,
  )


  final DateTime? completesBy;





    @override
    bool operator ==(Object other) => identical(this, other) || other is AccountDeletion &&
      other.state == state &&
      other.completesBy == completesBy;

    @override
    int get hashCode =>
        state.hashCode +
        (completesBy == null ? 0 : completesBy.hashCode);

  factory AccountDeletion.fromJson(Map<String, dynamic> json) => _$AccountDeletionFromJson(json);

  Map<String, dynamic> toJson() => _$AccountDeletionToJson(this);

  @override
  String toString() {
    return toJson().toString();
  }

}

/// deferred_review = content is under moderation review; the hard delete waits for it
enum AccountDeletionStateEnum {
    /// deferred_review = content is under moderation review; the hard delete waits for it
@JsonValue(r'scheduled')
scheduled(r'scheduled'),
    /// deferred_review = content is under moderation review; the hard delete waits for it
@JsonValue(r'deferred_review')
deferredReview(r'deferred_review');

const AccountDeletionStateEnum(this.value);

final String value;

@override
String toString() => value;
}


