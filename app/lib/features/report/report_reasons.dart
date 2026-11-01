import 'package:vela_api_client/vela_api_client.dart';

/// Why a viewer reports something. Order is the order shown in the sheet.
enum ReportReason {
  illegalContent(
    ReportRequestReasonEnum.illegalContent,
    'Illegal content',
    'Child safety, threats, or anything unlawful.',
  ),
  copyright(
    ReportRequestReasonEnum.copyright,
    'Copyright',
    'Work that someone else made and owns.',
  ),
  harassment(
    ReportRequestReasonEnum.harassment,
    'Harassment',
    'Bullying, hate, or unwanted contact.',
  ),
  spam(ReportRequestReasonEnum.spam, 'Spam', 'Ads, scams, or repeat posts.'),
  other(ReportRequestReasonEnum.other, 'Other', 'Something else is wrong.');

  const ReportReason(this.api, this.label, this.hint);

  final ReportRequestReasonEnum api;
  final String label;
  final String hint;
}

/// Longest note the API accepts (UTF-16 code units).
const int reportNoteMaxLength = 500;

ReportRequestTargetTypeEnum reportTargetType(String type) => switch (type) {
  'work' => ReportRequestTargetTypeEnum.work,
  'reply' => ReportRequestTargetTypeEnum.reply,
  'user' => ReportRequestTargetTypeEnum.user,
  _ => throw ArgumentError.value(type, 'targetType', 'work, reply or user'),
};
