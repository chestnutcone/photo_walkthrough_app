import '../../core/api/api_exception.dart';

import 'package:vela_api_client/vela_api_client.dart';

import 'pin_draft.dart';

const maxPinsMessage = 'A story can have at most $maxPins chapters.';

const blockedMessage =
    "This photo can't be published (content policy). It has been sent for review.";
const removedMessage = 'Removed by moderators';
const stillProcessingMessage =
    "This story is still processing. Check back later; it will appear in your portfolio when it's ready.";
const unsupportedTypeMessage = 'Choose a JPEG, PNG or WebP photo.';
const tooLargeMessage = 'That photo is too large. Choose one under 20 MB.';

String quotaMessage({required int used, required int max}) =>
    'You have used $used of $max stories. Delete one to add more.';

/// The limit the server quoted in its quota error ("Story limit reached
/// (20)."), or null.
int? quotaLimitFromMessage(String message) {
  final m = RegExp(r'\((\d+)\)').firstMatch(message);
  return m == null ? null : int.tryParse(m.group(1)!);
}

/// User-facing text for a failed create-flow call. Never echoes server text
/// (it may name fields) beyond the fixed strings below.
String createErrorMessage(ApiException e) => switch (e.code) {
  'network_error' => "Can't reach the server. Check your connection.",
  'rate_limited' => 'Too many tries. Wait a moment and try again.',
  'draft_expired' => 'This draft expired after 24 hours. Start again.',
  'invalid_image' => "That photo couldn't be used. Choose another.",
  'image_missing' => "The photo didn't finish uploading. Try again.",
  'no_chapters' => 'Add at least one chapter before publishing.',
  'validation_failed' =>
    'Some text is too long or missing. Check your title and chapters.',
  'not_editable' => "This story can't be changed any more.",
  'onboarding_required' => 'Finish setting up your profile first.',
  'attestation_required' ||
  'attestation_failed' => "This device couldn't be verified.",
  'payload_too_large' => tooLargeMessage,
  _ => 'Something went wrong. Try again.',
};

/// What the owner sees instead of the story when moderation took it down.
/// Only the owner (and admins) are sent a status; everyone else gets a 404.
String? ownerStatusNotice(Work w) {
  if (!w.isOwner) return null;
  return switch (w.status) {
    WorkStatusEnum.removed => removedMessage,
    WorkStatusEnum.blocked => blockedMessage,
    _ => null,
  };
}
