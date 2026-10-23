import 'package:vela_api_client/vela_api_client.dart';

import '../../core/api/api_exception.dart';
import 'portfolio_format.dart';

/// Contract limits (SendInquiryRequest).
const int kInquiryMaxMessage = 2000;
const int kInquiryMaxEmail = 254;

/// The five inquiry types, in display order.
const inquiryTypes = <(String, SendInquiryRequestTypeEnum)>[
  ('Commission', SendInquiryRequestTypeEnum.commission),
  ('Buy a print', SendInquiryRequestTypeEnum.buyAPrint),
  ('Licensing', SendInquiryRequestTypeEnum.licensing),
  ('Collaboration', SendInquiryRequestTypeEnum.collaboration),
  ('Just saying hi', SendInquiryRequestTypeEnum.justSayingHi),
];

SendInquiryRequestTypeEnum inquiryTypeFor(String? label) => inquiryTypes
    .firstWhere(
      (t) => t.$1.toLowerCase() == label?.trim().toLowerCase(),
      orElse: () => inquiryTypes.first,
    )
    .$2;

String inquiryLabel(SendInquiryRequestTypeEnum type) =>
    inquiryTypes.firstWhere((t) => t.$2 == type).$1;

/// A sender address the server can plausibly accept: something, one "@",
/// something, no spaces. The server has the final say.
bool isPlausibleEmail(String raw) {
  final email = raw.trim();
  return email.length <= kInquiryMaxEmail &&
      RegExp(r'^[^\s@]+@[^\s@]+$').hasMatch(email);
}

/// Trimmed, control characters other than new lines removed (the contract
/// rejects them), capped at the contract length.
String cleanInquiryMessage(String raw) {
  final cleaned = raw
      .replaceAll('\r\n', '\n')
      .replaceAll(RegExp(r'[\u0000-\u0009\u000B-\u001F\u007F]'), '')
      .trim();
  return cleaned.length <= kInquiryMaxMessage
      ? cleaned
      : cleaned.substring(0, kInquiryMaxMessage);
}

bool canSendInquiry({required String email, required String message}) =>
    isPlausibleEmail(email) && cleanInquiryMessage(message).isNotEmpty;

/// Hint text per type (README: "placeholder changes by type").
String messageHint(SendInquiryRequestTypeEnum type) => switch (type) {
  SendInquiryRequestTypeEnum.commission =>
    'Tell them about the project, timing and budget.',
  SendInquiryRequestTypeEnum.buyAPrint =>
    'Which size or edition are you after?',
  SendInquiryRequestTypeEnum.licensing =>
    'Where will the image be used, and for how long?',
  SendInquiryRequestTypeEnum.collaboration =>
    'What would you like to make together?',
  _ => 'Say hello. No pitch needed.',
};

/// The phrase in the success summary: "Your commission inquiry".
String inquirySummaryNoun(SendInquiryRequestTypeEnum type) => switch (type) {
  SendInquiryRequestTypeEnum.commission => 'commission inquiry',
  SendInquiryRequestTypeEnum.buyAPrint => 'print inquiry',
  SendInquiryRequestTypeEnum.licensing => 'licensing inquiry',
  SendInquiryRequestTypeEnum.collaboration => 'collaboration inquiry',
  _ => 'message',
};

/// User-facing text for a failed send. Never includes what the user typed.
String inquiryErrorText(ApiException e) {
  switch (e.code) {
    case 'network_error':
      return "You're offline. Your message is still here. Try again when "
          "you're back online.";
    case 'rate_limited':
      final wait = e.retryAfter;
      return wait == null
          ? 'You have sent a lot of inquiries. Try again in a little while.'
          : 'You have sent a lot of inquiries. Try again in ${waitLabel(wait)}.';
    case 'unauthorized':
      return 'Sign in again to send your inquiry. Your message is still here.';
    case 'validation_failed':
    case 'bad_request':
      return 'Check the email address and message, then try again.';
  }
  if (e.statusCode == 429) {
    final wait = e.retryAfter;
    return wait == null
        ? 'You have sent a lot of inquiries. Try again in a little while.'
        : 'You have sent a lot of inquiries. Try again in ${waitLabel(wait)}.';
  }
  return "Couldn't send your inquiry. Your message is still here. Try again.";
}
