import 'dart:typed_data';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'package:vela_app/features/create/photo_picker.dart';

import 'support/edit_harness.dart';

FilledButton _save(WidgetTester t) => t.widget(key('edit-save'));

void main() {
  testWidgets('bio_counter_blocks_over_280', (tester) async {
    await pumpEdit(tester, path: '/profile/edit');
    await typeInto(tester, 'edit-bio', 'a' * 280);
    expect(find.text('280/280'), findsOneWidget);
    expect(_save(tester).onPressed, isNotNull);

    await typeInto(tester, 'edit-bio', 'a' * 281);
    expect(find.text('281/280'), findsOneWidget);
    expect(find.textContaining('1 over'), findsWidgets);
    expect(_save(tester).onPressed, isNull);

    // Emoji count once, like the server.
    await typeInto(tester, 'edit-bio', '😀' * 280);
    expect(find.text('280/280'), findsOneWidget);
    expect(_save(tester).onPressed, isNotNull);
  });

  group('handle', () {
    testWidgets('handle_field_validates_and_shows_30_day_lock_message', (
      tester,
    ) async {
      final h = await pumpEdit(tester, path: '/profile/edit');
      expect(find.textContaining('once every 30 days'), findsOneWidget);

      await typeInto(tester, 'edit-handle', 'ab');
      expect(find.textContaining('at least 3'), findsOneWidget);
      expect(_save(tester).onPressed, isNull);

      await typeInto(tester, 'edit-handle', 'ana_new');
      await settle(tester);
      expect(find.textContaining('@ana_new is available'), findsOneWidget);
      expect(_save(tester).onPressed, isNotNull);

      h.api.patchStatus = 409;
      h.api.patchCode = 'handle_change_too_soon';
      await tester.tap(key('edit-save'));
      await settle(tester);
      await tester.tap(key('handle-confirm-yes'));
      await settle(tester);
      expect(
        find.textContaining('You changed your handle recently'),
        findsOneWidget,
      );
      expect(_save(tester).onPressed, isNull);
      // Edits are kept.
      expect(find.widgetWithText(TextField, 'ana_new'), findsOneWidget);
    });

    testWidgets('own_current_handle_needs_no_lookup', (tester) async {
      final h = await pumpEdit(tester, path: '/profile/edit');
      await typeInto(tester, 'edit-handle', 'ana_x');
      await settle(tester);
      await typeInto(tester, 'edit-handle', 'ana');
      await settle(tester);
      expect(h.api.all.where((r) => r.path == '/v1/artists/ana'), isEmpty);
      expect(find.textContaining('taken'), findsNothing);
    });

    testWidgets('taken_handle_blocks_save', (tester) async {
      await pumpEdit(
        tester,
        path: '/profile/edit',
        api: EditApi()..taken = {'ines'},
      );
      await typeInto(tester, 'edit-handle', 'ines');
      await settle(tester);
      expect(find.textContaining('already taken'), findsOneWidget);
      expect(_save(tester).onPressed, isNull);
    });

    testWidgets('changed_handle_needs_confirmation_and_sends_new_handle', (
      tester,
    ) async {
      final h = await pumpEdit(tester, path: '/profile/edit');
      await typeInto(tester, 'edit-handle', 'ana_new');
      await settle(tester);
      await tester.tap(key('edit-save'));
      await settle(tester);
      expect(find.textContaining('Links to your old handle'), findsOneWidget);
      await tester.tap(key('handle-confirm-no'));
      await settle(tester);
      expect(h.api.to('PATCH', '/v1/me/profile'), isEmpty);

      await tester.tap(key('edit-save'));
      await settle(tester);
      await tester.tap(key('handle-confirm-yes'));
      await settle(tester);
      expect(h.api.body(h.api.to('PATCH', '/v1/me/profile').single), {
        'handle': 'ana_new',
      });
      expect(h.location, '/artist/ana_new');
    });

    testWidgets('server_handle_taken_is_mapped', (tester) async {
      final h = await pumpEdit(tester, path: '/profile/edit');
      h.api
        ..patchStatus = 409
        ..patchCode = 'handle_taken';
      await typeInto(tester, 'edit-handle', 'ana_new');
      await settle(tester);
      await tester.tap(key('edit-save'));
      await settle(tester);
      await tester.tap(key('handle-confirm-yes'));
      await settle(tester);
      expect(find.textContaining('already taken'), findsOneWidget);
    });
  });

  group('save', () {
    testWidgets('save_sends_only_changed_fields_then_returns', (tester) async {
      final h = await pumpEdit(tester, path: '/profile/edit');
      await typeInto(tester, 'edit-city', 'Porto');
      await tester.tap(key('edit-save'));
      await settle(tester);
      expect(h.api.body(h.api.to('PATCH', '/v1/me/profile').single), {
        'city': 'Porto',
      });
      expect(h.location, '/');
    });

    testWidgets('save_is_disabled_until_something_changed', (tester) async {
      await pumpEdit(tester, path: '/profile/edit');
      expect(_save(tester).onPressed, isNull);
    });

    testWidgets('failed_save_keeps_edits_and_shows_inline_error', (
      tester,
    ) async {
      final h = await pumpEdit(tester, path: '/profile/edit');
      h.api.patchStatus = 500;
      h.api.patchCode = 'server_error';
      await typeInto(tester, 'edit-city', 'Porto');
      await tester.tap(key('edit-save'));
      await settle(tester);
      expect(key('edit-error'), findsOneWidget);
      expect(find.widgetWithText(TextField, 'Porto'), findsOneWidget);
      expect(h.location, '/profile/edit');
      expect(_save(tester).onPressed, isNotNull, reason: 'can retry');
    });

    testWidgets('save_disables_the_form_while_saving', (tester) async {
      final h = await pumpEdit(tester, path: '/profile/edit');
      await typeInto(tester, 'edit-city', 'Porto');
      await tester.tap(key('edit-save'));
      await tester.pump();
      expect(_save(tester).onPressed, isNull);
      expect(tester.widget<TextField>(key('edit-city')).enabled, isFalse);
      await settle(tester);
      expect(h.api.to('PATCH', '/v1/me/profile'), hasLength(1));
    });

    testWidgets('clearing_public_email_sends_null', (tester) async {
      final h = await pumpEdit(
        tester,
        path: '/profile/edit',
        api: EditApi(profile: myProfile(publicEmail: 'hi@ana.example')),
      );
      await typeInto(tester, 'edit-email', '');
      await tester.tap(key('edit-save'));
      await settle(tester);
      final body = h.api.body(h.api.to('PATCH', '/v1/me/profile').single);
      expect(body.containsKey('publicEmail'), isTrue);
      expect(body['publicEmail'], isNull);
    });

    testWidgets('invalid_email_and_website_block_save', (tester) async {
      await pumpEdit(tester, path: '/profile/edit');
      await typeInto(tester, 'edit-email', 'nope');
      expect(_save(tester).onPressed, isNull);
      await typeInto(tester, 'edit-email', 'ok@ana.example');
      await typeInto(tester, 'edit-website', 'http://ana.example');
      expect(_save(tester).onPressed, isNull);
      await typeInto(tester, 'edit-website', 'https://ana.example');
      expect(_save(tester).onPressed, isNotNull);
    });

    testWidgets('public_email_note_says_login_email_is_never_shown', (
      tester,
    ) async {
      await pumpEdit(tester, path: '/profile/edit');
      expect(
        find.textContaining('sign-in email is never shown'),
        findsOneWidget,
      );
    });
  });

  group('unsaved changes', () {
    testWidgets('back_with_edits_asks_before_discarding', (tester) async {
      final h = await pumpEdit(tester, path: '/profile/edit');
      await typeInto(tester, 'edit-city', 'Porto');
      await tester.tap(key('edit-back'));
      await settle(tester);
      expect(find.text('Discard changes?'), findsOneWidget);
      await tester.tap(key('discard-no'));
      await settle(tester);
      expect(h.location, '/profile/edit');
      await tester.tap(key('edit-back'));
      await settle(tester);
      await tester.tap(key('discard-yes'));
      await settle(tester);
      expect(h.location, '/');
    });

    testWidgets('back_without_edits_leaves_at_once', (tester) async {
      final h = await pumpEdit(tester, path: '/profile/edit');
      await tester.tap(key('edit-back'));
      await settle(tester);
      expect(h.location, '/');
    });
  });

  testWidgets('avatar_upload_then_save_updates_profile', (tester) async {
    final h = await pumpEdit(tester, path: '/profile/edit');
    await tester.tap(key('edit-avatar'));
    await settle(tester);
    expect(h.picker.calls, 1);
    expect(find.textContaining('uploaded when you save'), findsOneWidget);
    expect(h.uploader.uploads, isEmpty, reason: 'nothing leaves before save');

    await tester.tap(key('edit-save'));
    await settle(tester);
    expect(h.api.to('POST', '/v1/me/avatar-upload'), hasLength(1));
    expect(h.uploader.uploads.single.contentType, 'image/png');
    expect(h.uploader.uploads.single.url.host, 'blob.test');
    expect(h.api.body(h.api.to('PATCH', '/v1/me/profile').single), {
      'avatarUploaded': true,
    });
    // The upload ran before the PATCH.
    final order = [for (final r in h.api.all) r.path];
    expect(
      order.indexOf('/v1/me/avatar-upload') < order.indexOf('/v1/me/profile'),
      isTrue,
    );
    expect(h.location, '/');
  });

  testWidgets('unsupported_avatar_is_refused_before_upload', (tester) async {
    final h = await pumpEdit(tester, path: '/profile/edit');
    h.picker.result = PickedPhoto(Uint8List.fromList(List.filled(32, 7)));
    await tester.tap(key('edit-avatar'));
    await settle(tester);
    expect(find.textContaining('JPEG, PNG or WebP'), findsOneWidget);
    expect(_save(tester).onPressed, isNull);
  });

  group('services and shows', () {
    testWidgets('services_can_be_added_edited_removed', (tester) async {
      final h = await pumpEdit(tester, path: '/profile/edit');
      await tapKey(tester, 'service-add');
      await tester.enterText(key('service-title'), 'Portrait session');
      await tester.enterText(key('service-price'), 'From \$400');
      await tester.tap(key('service-form-save'));
      await settle(tester);
      expect(find.text('Portrait session'), findsOneWidget);

      await tapKey(tester, 'service-edit-0');
      await tester.enterText(key('service-title'), 'Studio portrait');
      await tester.tap(key('service-form-save'));
      await settle(tester);
      expect(find.text('Studio portrait'), findsOneWidget);
      expect(find.text('Portrait session'), findsNothing);

      await tester.tap(key('edit-save'));
      await settle(tester);
      final body = h.api.body(h.api.to('PATCH', '/v1/me/profile').single);
      expect(body['services'], [
        {'title': 'Studio portrait', 'description': '', 'price': 'From \$400'},
      ]);
    });

    testWidgets('removing_a_service_sends_the_shorter_list', (tester) async {
      final h = await pumpEdit(
        tester,
        path: '/profile/edit',
        api: EditApi(
          profile: myProfile(
            services: [
              {'title': 'A', 'description': '', 'price': ''},
              {'title': 'B', 'description': '', 'price': ''},
            ],
          ),
        ),
      );
      await tapKey(tester, 'service-remove-0');
      await tester.tap(key('edit-save'));
      await settle(tester);
      expect(
        h.api.body(h.api.to('PATCH', '/v1/me/profile').single)['services'],
        hasLength(1),
      );
    });

    testWidgets('service_needs_a_title', (tester) async {
      await pumpEdit(tester, path: '/profile/edit');
      await tapKey(tester, 'service-add');
      await tester.tap(key('service-form-save'));
      await settle(tester);
      expect(key('service-form-problem'), findsOneWidget);
      expect(key('service-title'), findsOneWidget, reason: 'sheet stays open');
    });

    testWidgets('services_stop_at_the_limit', (tester) async {
      await pumpEdit(
        tester,
        path: '/profile/edit',
        api: EditApi(
          profile: myProfile(
            services: [
              for (var i = 0; i < 10; i++)
                {'title': 'S$i', 'description': '', 'price': ''},
            ],
          ),
        ),
      );
      await tester.ensureVisible(key('service-add'));
      expect(
        tester.widget<OutlinedButton>(key('service-add')).onPressed,
        isNull,
      );
    });

    testWidgets('shows_can_be_added_with_a_year_and_removed', (tester) async {
      final h = await pumpEdit(tester, path: '/profile/edit');
      await tapKey(tester, 'show-add');
      await tester.enterText(key('show-year'), '20');
      await tester.enterText(key('show-text'), 'Group show');
      await tester.tap(key('show-form-save'));
      await settle(tester);
      expect(find.textContaining('4 digits'), findsOneWidget);
      await tester.enterText(key('show-year'), '2024');
      await tester.tap(key('show-form-save'));
      await settle(tester);
      expect(find.text('2024'), findsOneWidget);

      await tester.tap(key('edit-save'));
      await settle(tester);
      expect(h.api.body(h.api.to('PATCH', '/v1/me/profile').single)['shows'], [
        {'year': '2024', 'text': 'Group show'},
      ]);
    });
  });
}
