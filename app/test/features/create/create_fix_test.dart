import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:vela_app/core/api/api_exception.dart';
import 'package:vela_app/features/create/blob_uploader.dart';
import 'package:vela_app/features/create/create_controller.dart';
import 'package:vela_app/features/create/create_messages.dart';
import 'package:vela_app/features/create/create_state.dart';
import 'package:vela_app/features/create/photo_picker.dart';
import 'package:vela_app/features/create/utf16_limit.dart';

import '../../core/support/fakes.dart';
import 'support/create_harness.dart';

CreateState read(CreateHarness h) => h.container.read(createControllerProvider);

Uint8List hugePng() {
  final b = Uint8List.fromList(tinyPng);
  ByteData.sublistView(b)
    ..setUint32(16, 9000)
    ..setUint32(20, 9000); // 81 MP
  return b;
}

void main() {
  group('lost publish response', () {
    testWidgets('retry_after_a_lost_publish_response_goes_to_polling', (
      t,
    ) async {
      var published = false;
      final h = await pumpCreate(
        t,
        statuses: const ['pending', 'published'],
        intercept: (o) {
          if (o.method == 'POST' && o.path == '/v1/works/w1/publish') {
            published = true; // the server accepted it...
            throw DioException.connectionError(
              requestOptions: o,
              reason: 'lost',
            ); // ...but the answer never arrived
          }
          if (o.method == 'PUT' && published) {
            return jsonResponse(409, errEnvelope('not_editable'));
          }
          return null;
        },
      );
      await pickAndAnnotate(t);
      await addChapter(t, title: 'One');
      await t.tap(find.byKey(const ValueKey('publish')));
      await t.pumpAndSettle();
      expect(read(h).phase, CreatePhase.annotate);

      await t.tap(find.byKey(const ValueKey('publish')));
      await t.pumpAndSettle();
      expect(find.text('route:story/w1'), findsOneWidget);
      expect(h.calls('POST', '/v1/works/w1/publish'), hasLength(1));
      expect(h.calls('DELETE', '/v1/works/w1'), isEmpty);
    });

    testWidgets(
      'cancel_after_a_lost_publish_response_never_deletes_the_story',
      (t) async {
        final h = await pumpCreate(
          t,
          statuses: const ['pending'],
          intercept: (o) {
            if (o.method == 'POST' && o.path == '/v1/works/w1/publish') {
              throw DioException.connectionError(
                requestOptions: o,
                reason: 'x',
              );
            }
            return null;
          },
        );
        await pickAndAnnotate(t);
        await addChapter(t, title: 'One');
        await t.tap(find.byKey(const ValueKey('publish')));
        await t.pumpAndSettle();

        await t.tap(find.byKey(const ValueKey('cancel')));
        await t.pumpAndSettle();
        await t.tap(find.byKey(const ValueKey('confirm-discard')));
        await t.pumpAndSettle();
        expect(find.text('route:feed'), findsOneWidget);
        expect(h.calls('DELETE', '/v1/works/w1'), isEmpty);
      },
    );

    testWidgets('cancel_after_a_publish_the_server_refused_frees_the_draft', (
      t,
    ) async {
      final h = await pumpCreate(
        t,
        statuses: const ['draft'],
        publishStatus: 400,
        publishCode: 'invalid_image',
      );
      await pickAndAnnotate(t);
      await addChapter(t, title: 'One');
      await t.tap(find.byKey(const ValueKey('publish')));
      await t.pumpAndSettle();
      await t.tap(find.byKey(const ValueKey('cancel')));
      await t.pumpAndSettle();
      await t.tap(find.byKey(const ValueKey('confirm-discard')));
      await t.pumpAndSettle();
      expect(h.calls('DELETE', '/v1/works/w1'), hasLength(1));
    });

    testWidgets('non_transient_poll_error_then_cancel_does_not_delete', (
      t,
    ) async {
      final h = await pumpCreate(
        t,
        intercept: (o) => o.method == 'GET' && o.path == '/v1/works/w1'
            ? jsonResponse(403, errEnvelope('forbidden'))
            : null,
      );
      await pickAndAnnotate(t);
      await addChapter(t, title: 'One');
      await t.tap(find.byKey(const ValueKey('publish')));
      await t.pumpAndSettle();
      expect(read(h).phase, CreatePhase.publishing);
      await t.tap(find.byKey(const ValueKey('cancel')));
      await t.pumpAndSettle();
      expect(h.calls('DELETE', '/v1/works/w1'), isEmpty);
      expect(find.text('route:feed'), findsOneWidget);
    });
  });

  group('leaving', () {
    testWidgets(
      'leaving_during_create_draft_deletes_the_draft_that_comes_back',
      (t) async {
        final gate = Completer<void>();
        final h = await pumpCreate(
          t,
          intercept: (o) => o.method == 'POST' && o.path == '/v1/works'
              ? gate.future.then(
                  (_) => jsonResponse(
                    200,
                    okEnvelope({
                      'workId': 'w1',
                      'uploadUrl': 'https://blob.test/w1?sig=S',
                      'expiresAt': '2099-01-01T00:00:00Z',
                    }),
                  ),
                )
              : null,
        );
        await t.tap(find.byKey(const ValueKey('pick-library')));
        await t.pump(const Duration(milliseconds: 50));
        await t.tap(find.byKey(const ValueKey('cancel')));
        await t.pumpAndSettle();
        expect(find.text('route:feed'), findsOneWidget);
        expect(h.calls('DELETE', '/v1/works/w1'), isEmpty);

        gate.complete();
        await t.pumpAndSettle();
        expect(h.calls('DELETE', '/v1/works/w1'), hasLength(1));
        expect(h.uploader.uploads, isEmpty);
      },
    );

    testWidgets('leaving_does_not_wait_for_a_slow_delete', (t) async {
      final never = Completer<ResponseBody>();
      final h = await pumpCreate(
        t,
        intercept: (o) => o.method == 'DELETE' ? never.future : null,
      );
      await pickAndAnnotate(t);
      await t.tap(find.byKey(const ValueKey('cancel')));
      await t.pumpAndSettle();
      expect(find.text('route:feed'), findsOneWidget);
      expect(h.calls('DELETE', '/v1/works/w1'), hasLength(1));
    });

    testWidgets('a_failed_delete_is_retried_once_in_the_background', (t) async {
      var n = 0;
      final h = await pumpCreate(
        t,
        intercept: (o) => o.method == 'DELETE' && ++n == 1
            ? jsonResponse(500, errEnvelope('server_error'))
            : null,
      );
      await pickAndAnnotate(t);
      await t.tap(find.byKey(const ValueKey('cancel')));
      await t.pumpAndSettle();
      expect(h.calls('DELETE', '/v1/works/w1'), hasLength(2));
    });

    testWidgets(
      'choose_a_different_photo_returns_to_pick_and_frees_the_draft',
      (t) async {
        final h = await pumpCreate(t);
        await pickAndAnnotate(t);
        await t.tap(find.byKey(const ValueKey('change-photo')));
        await t.pumpAndSettle();
        expect(read(h).phase, CreatePhase.pick);
        expect(read(h).workId, isNull);
        expect(h.calls('DELETE', '/v1/works/w1'), hasLength(1));
        await pickAndAnnotate(t);
        expect(read(h).phase, CreatePhase.annotate);
        expect(h.calls('POST', '/v1/works'), hasLength(2));
      },
    );
  });

  group('photo checks', () {
    testWidgets('photo_over_60_megapixels_is_refused_at_pick_time', (t) async {
      final h = await pumpCreate(t, photo: hugePng());
      await pickAndAnnotate(t);
      expect(find.text(tooManyPixelsMessage), findsOneWidget);
      expect(h.calls('POST', '/v1/works'), isEmpty);
      expect(read(h).phase, CreatePhase.pick);
    });

    testWidgets('denied_photo_access_has_its_own_message', (t) async {
      final h = await pumpCreate(t);
      h.picker.error = const PhotoAccessDenied();
      await pickAndAnnotate(t);
      expect(find.text(photoAccessDeniedMessage), findsOneWidget);
    });

    test('picker_platform_errors_map_to_access_denied', () {
      expect(
        mapPickerError(PlatformException(code: 'photo_access_denied')),
        isA<PhotoAccessDenied>(),
      );
      expect(
        mapPickerError(PlatformException(code: 'camera_access_denied')),
        isA<PhotoAccessDenied>(),
      );
      final other = PlatformException(code: 'boom');
      expect(mapPickerError(other), same(other));
    });

    testWidgets(
      'expired_upload_url_drops_the_draft_and_retry_starts_a_new_one',
      (t) async {
        final h = await pumpCreate(t);
        h.uploader.failWith = const BlobUploadException(expired: true);
        await pickAndAnnotate(t);
        expect(read(h).uploadFailed, isTrue);
        expect(read(h).workId, isNull);
        expect(h.calls('DELETE', '/v1/works/w1'), hasLength(1));

        h.uploader.failWith = null;
        await t.tap(find.byKey(const ValueKey('retry-upload')));
        await t.pumpAndSettle();
        expect(h.calls('POST', '/v1/works'), hasLength(2));
        expect(read(h).uploadFailed, isFalse);
        expect(h.uploader.uploads, hasLength(1));
      },
    );
  });

  group('limits', () {
    test('utf16_formatter_never_exceeds_the_server_length', () {
      final f = Utf16LengthFormatter(120);
      final emoji = '😀' * 100; // 200 UTF-16 units
      final out = f.formatEditUpdate(
        TextEditingValue.empty,
        TextEditingValue(text: emoji),
      );
      expect(out.text.length, lessThanOrEqualTo(120));
      expect(out.text.length.isEven, isTrue, reason: 'no split surrogate');
      expect(
        'a' * 119 + '😀',
        isNot(
          f
              .formatEditUpdate(
                TextEditingValue.empty,
                TextEditingValue(text: 'a' * 119 + '😀'),
              )
              .text,
        ),
      );
    });

    testWidgets('chapter_title_field_counts_utf16_units', (t) async {
      final h = await pumpCreate(t);
      await pickAndAnnotate(t);
      await tapPhotoAt(t, 0.5, 0.5);
      await t.enterText(
        find.byKey(const ValueKey('chapter-title')),
        '😀' * 100,
      );
      await t.pump();
      await t.ensureVisible(find.byKey(const ValueKey('add-chapter')));
      await t.tap(find.byKey(const ValueKey('add-chapter')));
      await t.pumpAndSettle();
      expect(read(h).pins.single.title.length, lessThanOrEqualTo(120));
    });

    test('validation_failed_names_the_field_without_user_text', () {
      String msg(String m) => createErrorMessage(
        ApiException(code: 'validation_failed', message: m),
      );
      expect(
        msg('chapters[2].title must be 1-120 characters.'),
        contains('Chapter 3'),
      );
      expect(msg('title must be 1-200 characters.'), contains('story title'));
      expect(msg('???'), contains('Check'));
    });
  });

  testWidgets('pin_editor_decodes_the_photo_at_display_size', (t) async {
    await pumpCreate(t);
    await pickAndAnnotate(t);
    final image = t.widget<Image>(
      find.descendant(of: photoBox, matching: find.byType(Image)),
    );
    expect(image.image, isA<ResizeImage>());
    expect((image.image as ResizeImage).width, 390);
  });
}
