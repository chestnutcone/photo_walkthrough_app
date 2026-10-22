//
// AUTO-GENERATED FILE, DO NOT MODIFY!
//

// ignore_for_file: unused_element
import 'package:json_annotation/json_annotation.dart';

part 'mark_read_request.g.dart';


@JsonSerializable(
  checked: true,
  createToJson: true,
  disallowUnrecognizedKeys: false,
  explicitToJson: true,
)
class MarkReadRequest {
  /// Returns a new [MarkReadRequest] instance.
  MarkReadRequest({

     this.ids,

     this.all,
  });

  @JsonKey(
    
    name: r'ids',
    required: false,
    includeIfNull: false,
  )


  final List<String>? ids;



  @JsonKey(
    
    name: r'all',
    required: false,
    includeIfNull: false,
  )


  final bool? all;





    @override
    bool operator ==(Object other) => identical(this, other) || other is MarkReadRequest &&
      other.ids == ids &&
      other.all == all;

    @override
    int get hashCode =>
        (ids == null ? 0 : ids.hashCode) +
        all.hashCode;

  factory MarkReadRequest.fromJson(Map<String, dynamic> json) => _$MarkReadRequestFromJson(json);

  Map<String, dynamic> toJson() => _$MarkReadRequestToJson(this);

  @override
  String toString() {
    return toJson().toString();
  }

}

