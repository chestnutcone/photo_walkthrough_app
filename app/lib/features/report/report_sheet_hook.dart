import 'package:flutter/widgets.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

/// Opens the report flow for one target. [targetType] is one of the API's
/// `work`, `reply` or `user`. Completes true when a report was filed, so the
/// caller can hide the target locally.
///
/// B14 overrides [reportSheetHook] with the real sheet.
typedef ShowReportSheet =
    Future<bool> Function(
      BuildContext context, {
      required String targetType,
      required String targetId,
    });

/// Default no-op: nothing is filed.
final reportSheetHook = Provider<ShowReportSheet>(
  (_) =>
      (context, {required targetType, required targetId}) async => false,
);
