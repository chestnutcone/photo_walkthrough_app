import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:vela_api_client/vela_api_client.dart';

import '../../core/api/api_exception.dart';
import '../../core/api/api_providers.dart';
import '../auth/onboarding_flag.dart';
import '../portfolio/portfolio_data.dart';
import 'avatar_picker.dart';
import 'onboarding_draft.dart';

/// Suggested artists for the follow step. Not retried automatically; the
/// screen offers an explicit retry.
final suggestedArtistsProvider = FutureProvider.autoDispose<List<Profile>>((
  ref,
) async {
  final res = await apiGuard(
    () => ref.read(apiProvider).getArtistsApi().suggestedArtists(limit: 20),
  );
  return unwrapEnvelope(
    success: res.data?.success ?? false,
    data: res.data?.data,
    error: res.data?.error,
  );
}, retry: (_, _) => null);

enum SubmitResult {
  /// Onboarding is complete and the session knows it.
  done,

  /// The server refused the handle; the draft is back on the profile step.
  handleRejected,

  /// Anything else; [SubmitOutcome.error] says what.
  failed,
}

class SubmitOutcome {
  const SubmitOutcome(this.result, {this.error, this.avatarFailed = false});

  final SubmitResult result;
  final ApiException? error;
  final bool avatarFailed;
}

final onboardingSubmitterProvider = Provider<OnboardingSubmitter>(
  OnboardingSubmitter.new,
);

class OnboardingSubmitter {
  OnboardingSubmitter(this._ref);

  final Ref _ref;

  /// Sends the profile, the 18+ and terms confirmations and [follows] in one
  /// call (the API takes them together), then uploads the avatar, which the
  /// API only accepts once onboarding is complete.
  Future<SubmitOutcome> submit(Set<String> follows) async {
    final draft = _ref.read(onboardingDraftProvider);
    try {
      await _complete(draft, follows);
    } on ApiException catch (e) {
      // The server already finished onboarding (another device or a retry
      // after a lost response): carry on as done.
      if (e.code != 'already_onboarded') return _failure(draft, e);
    }
    final avatarFailed =
        draft.avatar != null && !await _uploadAvatar(draft.avatar!);
    await _refreshMe();
    return SubmitOutcome(SubmitResult.done, avatarFailed: avatarFailed);
  }

  SubmitOutcome _failure(OnboardingDraft draft, ApiException e) {
    if (e.code == 'handle_taken' || e.code == 'invalid_handle') {
      _ref
          .read(onboardingDraftProvider.notifier)
          .rejectHandle(draft.handle, invalid: e.code == 'invalid_handle');
      return SubmitOutcome(SubmitResult.handleRejected, error: e);
    }
    return SubmitOutcome(SubmitResult.failed, error: e);
  }

  Future<void> _complete(OnboardingDraft d, Set<String> follows) async {
    final res = await apiGuard(
      () => _ref
          .read(apiProvider)
          .getMeApi()
          .completeOnboarding(
            completeOnboardingRequest: CompleteOnboardingRequest(
              displayName: d.displayName.trim(),
              handle: d.handle,
              ageConfirmed: d.ageConfirmed,
              termsAccepted: d.termsAccepted,
              followArtistIds: follows.toList(),
            ),
          ),
    );
    unwrapEnvelope(
      success: res.data?.success ?? false,
      data: res.data?.data,
      error: res.data?.error,
    );
  }

  /// Best effort: a failed photo never undoes a finished onboarding.
  Future<bool> _uploadAvatar(PickedAvatar avatar) async {
    try {
      final me = _ref.read(apiProvider).getMeApi();
      final ticket = unwrapEnvelope(
        success: true,
        data: (await apiGuard(() => me.avatarUpload())).data?.data,
        error: null,
      );
      // The signed URL carries its own authorisation: use a Dio without the
      // bearer interceptor so the access token never goes to blob storage.
      await _ref
          .read(blobDioProvider)
          .put<void>(
            ticket.uploadUrl,
            data: avatar.bytes,
            options: Options(
              contentType: avatar.contentType,
              headers: {
                'x-ms-blob-type': 'BlockBlob',
                Headers.contentLengthHeader: avatar.bytes.length,
              },
            ),
          );
      await apiGuard(
        () => me.updateProfile(
          updateProfileRequest: UpdateProfileRequest(avatarUploaded: true),
        ),
      );
      return true;
    } on Object {
      return false;
    }
  }

  /// Re-reads `/v1/me` for the rest of the app (best effort).
  Future<void> _refreshMe() async {
    // The server accepted onboarding: from here on this session is done,
    // whatever the refresh below returns.
    _ref.read(onboardedLocallyProvider.notifier).set(true);
    _ref.read(newUserHintProvider.notifier).set(false);
    _ref.invalidate(meProvider);
    try {
      await _ref.read(meProvider.future);
    } on Object {
      // intentionally ignored
    }
  }
}

/// Plain Dio for the blob upload: no base URL, no bearer interceptor.
final blobDioProvider = Provider<Dio>((ref) {
  final dio = Dio(
    BaseOptions(
      connectTimeout: const Duration(seconds: 10),
      sendTimeout: const Duration(seconds: 60),
      receiveTimeout: const Duration(seconds: 30),
    ),
  );
  final adapter = ref.read(httpClientAdapterProvider);
  if (adapter != null) dio.httpClientAdapter = adapter;
  return dio;
});
