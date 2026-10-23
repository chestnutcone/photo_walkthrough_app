import 'dart:typed_data';

import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:vela_app/core/auth/auth_controller.dart';
import 'package:vela_app/core/router.dart';
import 'package:vela_app/features/create/photo_picker.dart';
import 'package:vela_app/features/onboarding/legal_links.dart';

import '../../core/support/avatar_marker.dart';
import '../create/support/create_harness.dart' show FakePhotoPicker;
import 'support/onboarding_harness.dart';

String _location(OnboardingHarness h) {
  final router = h.container.read(routerProvider);
  return router.routerDelegate.currentConfiguration.uri.path;
}

Future<void> _tapContinue(WidgetTester tester) async {
  await tester.ensureVisible(key('profile-continue'));
  await tester.tap(key('profile-continue'));
  await settle(tester);
}

void main() {
  group('welcome', () {
    testWidgets('unfinished_account_lands_on_welcome', (tester) async {
      final h = await pumpOnboarding(tester);
      expect(_location(h), '/onboarding');
      expect(key('welcome-continue'), findsOneWidget);
      expect(key('welcome-demo'), findsOneWidget);
    });

    testWidgets('welcome_states_each_sign_in_method_is_its_own_account', (
      tester,
    ) async {
      await pumpOnboarding(tester);
      expect(find.textContaining('own account'), findsOneWidget);
    });

    testWidgets('no_notification_permission_prompt', (tester) async {
      await pumpOnboarding(tester);
      await tester.tap(key('welcome-continue'));
      await settle(tester);
      expect(
        find.textContaining(RegExp('notification', caseSensitive: false)),
        findsNothing,
      );
    });

    testWidgets('onboarded_account_never_sees_onboarding', (tester) async {
      final h = await pumpOnboarding(tester, onboarded: true);
      expect(_location(h), '/');
    });
  });

  group('profile step', () {
    testWidgets('onboarding_submit_disabled_without_age_and_terms', (
      tester,
    ) async {
      await pumpOnboarding(tester);
      await fillProfile(tester, age: false, terms: false);
      FilledButton cont() => tester.widget(key('profile-continue'));
      expect(cont().onPressed, isNull);

      await tester.ensureVisible(key('age-check'));
      await tester.tap(key('age-check'));
      await tester.pump();
      expect(cont().onPressed, isNull, reason: 'terms still missing');

      await tester.ensureVisible(key('terms-check'));
      await tester.tap(key('terms-check'));
      await tester.pump();
      expect(cont().onPressed, isNotNull);

      await tester.tap(key('age-check'));
      await tester.pump();
      expect(cont().onPressed, isNull, reason: 'age unchecked again');
    });

    testWidgets('submit_needs_a_display_name', (tester) async {
      await pumpOnboarding(tester);
      await fillProfile(tester, name: '   ');
      FilledButton cont() => tester.widget(key('profile-continue'));
      expect(cont().onPressed, isNull);
    });

    testWidgets('handle_shows_taken_error', (tester) async {
      await pumpOnboarding(tester, api: OnboardingApi(taken: {'ines'}));
      await fillProfile(tester, handle: 'ines');
      expect(find.textContaining('already taken'), findsOneWidget);
      FilledButton cont() => tester.widget(key('profile-continue'));
      expect(cont().onPressed, isNull);
    });

    testWidgets('free_handle_shows_available_and_enables_submit', (
      tester,
    ) async {
      await pumpOnboarding(tester);
      await fillProfile(tester, handle: 'ana_ruiz');
      expect(find.textContaining('available'), findsOneWidget);
      FilledButton cont() => tester.widget(key('profile-continue'));
      expect(cont().onPressed, isNotNull);
    });

    testWidgets('handle_format_errors_need_no_network', (tester) async {
      final h = await pumpOnboarding(tester);
      await tester.tap(key('welcome-continue'));
      await settle(tester);

      await typeHandle(tester, 'ab');
      expect(find.textContaining('at least 3'), findsOneWidget);
      await typeHandle(tester, 'a-b-c');
      expect(
        find.textContaining('letters, numbers and underscores'),
        findsOneWidget,
      );
      await typeHandle(tester, 'admin');
      expect(find.textContaining("isn't available"), findsOneWidget);
      expect(
        h.api.all.where((r) => r.path.startsWith('/v1/artists/')),
        isEmpty,
      );
    });

    testWidgets('uppercase_is_lowercased_as_typed', (tester) async {
      await pumpOnboarding(tester);
      await tester.tap(key('welcome-continue'));
      await settle(tester);
      await typeHandle(tester, '@Ana_R');
      final field = tester.widget<TextField>(key('handle-field'));
      expect(field.controller!.text, 'ana_r');
    });

    testWidgets('handle_lookup_waits_for_typing_to_pause', (tester) async {
      final h = await pumpOnboarding(tester);
      await tester.tap(key('welcome-continue'));
      await settle(tester);
      for (final s in ['abc', 'abcd', 'abcde']) {
        await tester.enterText(key('handle-field'), s);
        await tester.pump(const Duration(milliseconds: 100));
      }
      await tester.pump(const Duration(milliseconds: 600));
      await settle(tester);
      expect(h.api.to('/v1/artists/abcde'), hasLength(1));
      expect(h.api.to('/v1/artists/abc'), isEmpty);
      expect(h.api.to('/v1/artists/abcd'), isEmpty);
    });

    testWidgets('handle_lock_note_is_shown', (tester) async {
      await pumpOnboarding(tester);
      await tester.tap(key('welcome-continue'));
      await settle(tester);
      expect(find.textContaining('once every 30 days'), findsOneWidget);
    });

    testWidgets('checking_state_shows_while_the_lookup_is_pending', (
      tester,
    ) async {
      final api = OnboardingApi();
      await pumpOnboarding(tester, api: api);
      await tester.tap(key('welcome-continue'));
      await settle(tester);
      api.gates['zed'] = Completer<void>();
      await tester.enterText(key('handle-field'), 'zed');
      await tester.pump(const Duration(milliseconds: 600));
      expect(find.textContaining('Checking'), findsOneWidget);
      api.gates['zed']!.complete();
      await settle(tester);
    });

    testWidgets('terms_and_privacy_links_open_the_legal_urls', (tester) async {
      final opened = <Uri>[];
      await pumpOnboarding(
        tester,
        overrides: [
          legalLinkOpenerProvider.overrideWithValue((context, uri) async {
            opened.add(uri);
          }),
        ],
      );
      await tester.tap(key('welcome-continue'));
      await settle(tester);
      await tester.ensureVisible(key('terms-link'));
      await tester.tap(key('terms-link'));
      await tester.ensureVisible(key('privacy-link'));
      await tester.tap(key('privacy-link'));
      expect(opened.map((u) => u.path), ['/legal/terms', '/legal/privacy']);
      expect(opened.first.scheme, startsWith('http'));
    });

    testWidgets('default_opener_launches_externally_and_reports_failure', (
      tester,
    ) async {
      final launched = <Uri>[];
      var ok = true;
      await pumpOnboarding(
        tester,
        overrides: [
          legalUrlLauncherProvider.overrideWithValue((uri) async {
            launched.add(uri);
            return ok;
          }),
        ],
      );
      await tester.tap(key('welcome-continue'));
      await settle(tester);
      await tester.ensureVisible(key('terms-link'));
      await tester.tap(key('terms-link'));
      await tester.pump();
      expect(launched.single.path, '/legal/terms');
      expect(find.textContaining("Couldn't open"), findsNothing);
      await tester.pumpAndSettle(const Duration(seconds: 5));

      ok = false;
      await tester.tap(key('privacy-link'));
      await tester.pump();
      expect(find.textContaining("Couldn't open"), findsOneWidget);
    });

    testWidgets('avatar_is_optional_and_previewed_when_picked', (tester) async {
      final picker = FakePhotoPicker(PickedPhoto(png));
      await pumpOnboarding(tester, picker: picker);
      await fillProfile(tester);
      FilledButton cont() => tester.widget(key('profile-continue'));
      expect(cont().onPressed, isNotNull, reason: 'avatar not required');
      await tester.ensureVisible(key('avatar-button'));
      await tester.tap(key('avatar-button'));
      await settle(tester);
      expect(picker.calls, 1);
      expect(find.byType(Image), findsWidgets);
    });
  });

  group('sign out', () {
    testWidgets('every_onboarding_step_can_sign_out_to_the_feed', (
      tester,
    ) async {
      for (final step in ['welcome', 'profile', 'follow']) {
        final h = await pumpOnboarding(tester);
        if (step != 'welcome') {
          await fillProfile(tester);
          if (step == 'follow') await _tapContinue(tester);
        }
        await tester.ensureVisible(key('onboarding-signout'));
        expect(find.textContaining('Not 18 or wrong account?'), findsOneWidget);
        await tester.tap(key('onboarding-signout'));
        await settle(tester);
        expect(_location(h), '/', reason: step);
        expect(h.storage.value, isNull, reason: step);
        expect(key('welcome-continue'), findsNothing, reason: step);
        await tester.pumpWidget(const SizedBox());
      }
    });

    testWidgets('sign_out_survives_a_failing_logout_call', (tester) async {
      final h = await pumpOnboarding(
        tester,
        api: OnboardingApi(logoutFails: true),
      );
      await tester.tap(key('onboarding-signout'));
      await settle(tester);
      expect(h.storage.value, isNull);
      expect(_location(h), '/');
    });
  });

  group('after completion', () {
    testWidgets('failed_me_refresh_does_not_send_a_finished_user_back', (
      tester,
    ) async {
      final h = await pumpOnboarding(
        tester,
        api: OnboardingApi(suggestedCount: 0, meFailsAfterSubmit: true),
      );
      await fillProfile(tester);
      await _tapContinue(tester);
      await tester.tap(key('follow-finish'));
      await settle(tester);
      expect(_location(h), '/');
      expect(h.container.read(needsOnboardingProvider), isFalse);
    });

    testWidgets('stale_me_refresh_does_not_send_a_finished_user_back', (
      tester,
    ) async {
      final h = await pumpOnboarding(
        tester,
        api: OnboardingApi(suggestedCount: 0, meStaleAfterSubmit: true),
      );
      await fillProfile(tester);
      await _tapContinue(tester);
      await tester.tap(key('follow-finish'));
      await settle(tester);
      expect(_location(h), '/');
      expect(h.container.read(needsOnboardingProvider), isFalse);
    });

    testWidgets('a_new_session_is_gated_again_after_sign_out', (tester) async {
      final h = await pumpOnboarding(
        tester,
        api: OnboardingApi(suggestedCount: 0, meStaleAfterSubmit: true),
      );
      await fillProfile(tester);
      await _tapContinue(tester);
      await tester.tap(key('follow-finish'));
      await settle(tester);
      await h.container.read(sessionProvider.notifier).clear();
      await h.container
          .read(sessionProvider.notifier)
          .signIn(accessToken: 'A2', refreshToken: 'R2', expiresIn: 900);
      await settle(tester);
      expect(h.container.read(needsOnboardingProvider), isTrue);
    });
  });

  group('follow step', () {
    testWidgets(
      'follow_step_requires_three_selections_but_is_skippable_with_discover_fallback',
      (tester) async {
        final h = await pumpOnboarding(tester);
        await fillProfile(tester);
        await _tapContinue(tester);
        FilledButton finish() => tester.widget(key('follow-finish'));
        expect(finish().onPressed, isNull);
        expect(find.textContaining('0 of 3'), findsOneWidget);

        await tester.tap(key('artist-s1'));
        await tester.tap(key('artist-s2'));
        await tester.pump();
        expect(finish().onPressed, isNull);
        expect(find.textContaining('2 of 3'), findsOneWidget);

        await tester.tap(key('artist-s3'));
        await tester.pump();
        expect(finish().onPressed, isNotNull);

        // Deselect again: back below the minimum.
        await tester.tap(key('artist-s3'));
        await tester.pump();
        expect(finish().onPressed, isNull);

        // Skip: nothing followed, Following falls back to Discover.
        expect(find.textContaining('Discover'), findsWidgets);
        await tester.tap(key('follow-skip'));
        await settle(tester);
        final body = h.api.body(h.api.to('/v1/me/onboarding').single);
        expect(body['followArtistIds'], isEmpty);
        expect(_location(h), '/');
      },
    );

    testWidgets('three_selections_are_sent_with_the_profile', (tester) async {
      final h = await pumpOnboarding(tester);
      await fillProfile(tester, name: ' Ana Ruiz ', handle: 'ana');
      await _tapContinue(tester);
      for (final id in ['s1', 's3', 's4']) {
        await tester.tap(key('artist-$id'));
      }
      await tester.pump();
      await tester.tap(key('follow-finish'));
      await settle(tester);

      final body = h.api.body(h.api.to('/v1/me/onboarding').single);
      expect(body['displayName'], 'Ana Ruiz');
      expect(body['handle'], 'ana');
      expect(body['ageConfirmed'], isTrue);
      expect(body['termsAccepted'], isTrue);
      expect(body['followArtistIds'], unorderedEquals(['s1', 's3', 's4']));
      expect(_location(h), '/');
      expect(h.container.read(needsOnboardingProvider), isFalse);
    });

    testWidgets('fewer_than_three_suggestions_lower_the_bar', (tester) async {
      await pumpOnboarding(tester, api: OnboardingApi(suggestedCount: 2));
      await fillProfile(tester);
      await _tapContinue(tester);
      FilledButton finish() => tester.widget(key('follow-finish'));
      expect(finish().onPressed, isNull);
      await tester.tap(key('artist-s1'));
      await tester.tap(key('artist-s2'));
      await tester.pump();
      expect(finish().onPressed, isNotNull);
    });

    testWidgets('no_suggestions_means_finish_is_open', (tester) async {
      await pumpOnboarding(tester, api: OnboardingApi(suggestedCount: 0));
      await fillProfile(tester);
      await _tapContinue(tester);
      FilledButton finish() => tester.widget(key('follow-finish'));
      expect(finish().onPressed, isNotNull);
    });

    testWidgets('suggestion_failure_offers_retry_and_skip', (tester) async {
      final api = OnboardingApi(suggestedStatus: 500);
      final h = await pumpOnboarding(tester, api: api);
      await fillProfile(tester);
      await _tapContinue(tester);
      expect(key('follow-retry'), findsOneWidget);
      expect(key('follow-skip'), findsOneWidget);
      api.suggestedStatus = 200;
      await tester.tap(key('follow-retry'));
      await settle(tester);
      expect(key('artist-s1'), findsOneWidget);
      expect(h.api.to('/v1/artists/suggested'), hasLength(2));
    });

    testWidgets('double_tap_on_finish_sends_one_request', (tester) async {
      final h = await pumpOnboarding(
        tester,
        api: OnboardingApi(suggestedCount: 0),
      );
      await fillProfile(tester);
      await _tapContinue(tester);
      await tester.tap(key('follow-finish'));
      await tester.tap(key('follow-finish'), warnIfMissed: false);
      await settle(tester);
      expect(h.api.to('/v1/me/onboarding'), hasLength(1));
    });
  });

  group('submit errors', () {
    testWidgets('server_handle_taken_returns_to_the_profile_step', (
      tester,
    ) async {
      await pumpOnboarding(
        tester,
        api: OnboardingApi(onboardingStatus: 409, suggestedCount: 0),
      );
      await fillProfile(tester);
      await _tapContinue(tester);
      await tester.tap(key('follow-finish'));
      await settle(tester);
      expect(key('name-field'), findsOneWidget);
      expect(find.textContaining('already taken'), findsOneWidget);
      FilledButton cont() => tester.widget(key('profile-continue'));
      expect(cont().onPressed, isNull);
    });

    testWidgets('server_invalid_handle_is_a_format_error_not_taken', (
      tester,
    ) async {
      await pumpOnboarding(
        tester,
        api: OnboardingApi(
          onboardingStatus: 400,
          onboardingCode: 'invalid_handle',
          suggestedCount: 0,
        ),
      );
      await fillProfile(tester);
      await _tapContinue(tester);
      await tester.tap(key('follow-finish'));
      await settle(tester);
      expect(key('name-field'), findsOneWidget);
      expect(find.textContaining('already taken'), findsNothing);
      expect(find.textContaining('lowercase letters'), findsOneWidget);
      FilledButton cont() => tester.widget(key('profile-continue'));
      expect(cont().onPressed, isNull);
    });

    testWidgets('already_onboarded_is_treated_as_done', (tester) async {
      final api = OnboardingApi(
        onboardingStatus: 409,
        onboardingCode: 'already_onboarded',
        suggestedCount: 0,
      );
      final h = await pumpOnboarding(tester, api: api);
      await fillProfile(tester);
      await _tapContinue(tester);
      api.onboarded = true; // the server finished it earlier
      await tester.tap(key('follow-finish'));
      await settle(tester);
      expect(_location(h), '/');
    });

    testWidgets('network_failure_keeps_the_form_and_allows_retry', (
      tester,
    ) async {
      final api = OnboardingApi(
        onboardingStatus: 500,
        onboardingCode: 'server_error',
        suggestedCount: 0,
      );
      final h = await pumpOnboarding(tester, api: api);
      await fillProfile(tester);
      await _tapContinue(tester);
      await tester.tap(key('follow-finish'));
      await settle(tester);
      expect(key('follow-error'), findsOneWidget);
      expect(_location(h), '/onboarding');
      api.onboardingStatus = 200;
      await tester.tap(key('follow-finish'));
      await settle(tester);
      expect(_location(h), '/');
    });
  });

  group('avatar', () {
    testWidgets('avatar_uploads_after_onboarding_without_the_bearer_token', (
      tester,
    ) async {
      final picker = FakePhotoPicker(PickedPhoto(png));
      final h = await pumpOnboarding(
        tester,
        picker: picker,
        api: OnboardingApi(suggestedCount: 0),
      );
      await fillProfile(tester);
      await tester.ensureVisible(key('avatar-button'));
      await tester.tap(key('avatar-button'));
      await settle(tester);
      await _tapContinue(tester);
      await tester.tap(key('follow-finish'));
      await settle(tester);

      expect(h.api.order, ['onboarding', 'ticket', 'upload', 'profile']);
      final put = h.api.all.firstWhere((r) => r.uri.host == 'blob.test');
      expect(put.method, 'PUT');
      expect(put.headers['x-ms-blob-type'], 'BlockBlob');
      expect(put.headers['Content-Type'], 'image/png');
      expect(put.headers.containsKey('Authorization'), isFalse);
      // The uploaded bytes went through the shared photo normalizer.
      expect(hasMarker(put.data as List<int>, png), isTrue);
      expect(h.api.body(h.api.to('/v1/me/profile').single), {
        'avatarUploaded': true,
      });
      expect(_location(h), '/');
    });

    testWidgets('unsupported_avatar_is_refused_before_upload', (tester) async {
      final picker = FakePhotoPicker(
        PickedPhoto(Uint8List.fromList(List.filled(32, 7))),
      );
      final h = await pumpOnboarding(tester, picker: picker);
      await fillProfile(tester);
      await tester.ensureVisible(key('avatar-button'));
      await tester.tap(key('avatar-button'));
      await settle(tester);
      expect(find.textContaining('JPEG, PNG or WebP'), findsOneWidget);
      expect(find.byType(Image), findsNothing);
      expect(h.api.all.where((r) => r.uri.host == 'blob.test'), isEmpty);
    });

    testWidgets('failed_avatar_upload_does_not_block_onboarding', (
      tester,
    ) async {
      final picker = FakePhotoPicker(PickedPhoto(png));
      final h = await pumpOnboarding(
        tester,
        picker: picker,
        api: OnboardingApi(suggestedCount: 0, uploadStatus: 403),
      );
      await fillProfile(tester);
      await tester.ensureVisible(key('avatar-button'));
      await tester.tap(key('avatar-button'));
      await settle(tester);
      await _tapContinue(tester);
      await tester.tap(key('follow-finish'));
      await settle(tester);
      expect(_location(h), '/');
      expect(h.api.to('/v1/me/profile'), isEmpty);
      expect(find.textContaining("couldn't upload your photo"), findsOneWidget);
    });

    testWidgets('no_avatar_means_no_upload_calls', (tester) async {
      final h = await pumpOnboarding(
        tester,
        api: OnboardingApi(suggestedCount: 0),
      );
      await fillProfile(tester);
      await _tapContinue(tester);
      await tester.tap(key('follow-finish'));
      await settle(tester);
      expect(h.api.order, ['onboarding']);
    });
  });

  testWidgets('back_buttons_walk_the_steps_and_keep_the_draft', (tester) async {
    await pumpOnboarding(tester);
    await fillProfile(tester, name: 'Ana Ruiz', handle: 'ana');
    await _tapContinue(tester);
    await tester.tap(key('follow-back'));
    await settle(tester);
    expect(
      tester.widget<TextField>(key('name-field')).controller!.text,
      'Ana Ruiz',
    );
    expect(
      tester.widget<TextField>(key('handle-field')).controller!.text,
      'ana',
    );
  });
}
