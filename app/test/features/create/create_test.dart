import 'dart:typed_data';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:image/image.dart' as img;
import 'package:vela_app/core/api/api_providers.dart';
import 'package:vela_app/features/create/create_controller.dart';
import 'package:vela_app/features/create/create_state.dart';
import 'package:vela_app/features/create/photo_picker.dart';

import 'support/create_harness.dart';

CreateState read(CreateHarness h) => h.container.read(createControllerProvider);

void main() {
  testWidgets('tap_places_ghost_pin_at_fraction', (t) async {
    final h = await pumpCreate(t);
    await pickAndAnnotate(t);
    expect(read(h).phase, CreatePhase.annotate);
    expect(h.uploader.uploads, hasLength(1));

    await tapPhotoAt(t, 0.25, 0.75);
    final ghost = read(h).ghost!;
    expect(ghost.x, closeTo(0.25, 0.01));
    expect(ghost.y, closeTo(0.75, 0.01));
    expect(find.byKey(const ValueKey('chapter-title')), findsOneWidget);
  });

  testWidgets('tap_outside_the_displayed_photo_places_nothing', (t) async {
    final h = await pumpCreate(t);
    await pickAndAnnotate(t);
    final area = t.getRect(find.byKey(const ValueKey('photo-area')));
    final photo = t.getRect(photoBox);
    expect(photo.top, greaterThan(area.top + 1)); // letterboxed
    await t.tapAt(Offset(area.center.dx, area.top + 2));
    await t.pump();
    expect(read(h).ghost, isNull);
  });

  testWidgets('add_chapter_requires_title_and_appends_in_order', (t) async {
    final h = await pumpCreate(t);
    await pickAndAnnotate(t);
    await tapPhotoAt(t, 0.2, 0.2);
    await t.ensureVisible(find.byKey(const ValueKey('add-chapter')));
    await t.tap(find.byKey(const ValueKey('add-chapter')));
    await t.pumpAndSettle();
    expect(read(h).pins, isEmpty, reason: 'no title, no chapter');

    await addChapter(t, title: 'First', fx: 0.2, fy: 0.2);
    await addChapter(t, title: 'Second', fx: 0.8, fy: 0.8);
    expect(read(h).pins.map((p) => p.title), ['First', 'Second']);
    expect(read(h).ghost, isNull);
  });

  testWidgets('stay_put_pin_serializes_zoom_null_and_zoom_in_serializes_2_4', (
    t,
  ) async {
    final h = await pumpCreate(t);
    await pickAndAnnotate(t);
    await addChapter(t, title: 'Zoomy', fx: 0.3, fy: 0.3);
    await addChapter(t, title: 'Still', fx: 0.6, fy: 0.6, stayPut: true);
    final chapters =
        h.container
                .read(createControllerProvider.notifier)
                .buildUpdateRequest()
                .toJson()['chapters']!
            as List;
    expect((chapters[0] as Map)['zoom'], 2.4);
    expect((chapters[1] as Map).containsKey('zoom'), isFalse);
    expect((chapters[1] as Map)['title'], 'Still');
  });

  testWidgets('forty_first_pin_is_refused', (t) async {
    final h = await pumpCreate(t);
    await pickAndAnnotate(t);
    final ctl = h.container.read(createControllerProvider.notifier);
    for (var i = 0; i < 40; i++) {
      ctl.placeGhost(const Offset(0.5, 0.5));
      expect(ctl.commitGhost(title: 'P$i', body: '', zoomIn: true), isTrue);
    }
    ctl.placeGhost(const Offset(0.1, 0.1));
    await t.pumpAndSettle();
    expect(read(h).pins, hasLength(40));
    expect(read(h).ghost, isNull);
    expect(find.text('A story can have at most 40 chapters.'), findsOneWidget);
  });

  testWidgets('quota_exceeded_shows_message_and_blocks_upload', (t) async {
    final h = await pumpCreate(t, createStatus: 409, used: 20, max: 20);
    await pickAndAnnotate(t);
    expect(
      find.text('You have used 20 of 20 stories. Delete one to add more.'),
      findsOneWidget,
    );
    expect(h.uploader.uploads, isEmpty);
    expect(read(h).phase, CreatePhase.pick);
  });

  testWidgets('quota_message_uses_the_real_numbers', (t) async {
    final h = await pumpCreate(
      t,
      createStatus: 409,
      createMessage: 'Story limit reached (5).',
      used: 5,
      max: 5,
    );
    await pickAndAnnotate(t);
    expect(
      find.text('You have used 5 of 5 stories. Delete one to add more.'),
      findsOneWidget,
    );
    expect(h.uploader.uploads, isEmpty);
  });

  testWidgets('exif_rotated_image_pins_map_to_displayed_orientation', (
    t,
  ) async {
    // Stored 40x30, orientation 6 (rotate 90 CW): the author sees 30x40.
    // The red block's centre is (5, 3) in stored pixels, so after the turn it
    // sits at (30 - 3, 5) = (27, 5) of the 30x40 image the server stores.
    final h = await pumpCreate(t, photo: jpegWithOrientation());
    await pickAndAnnotate(t);

    final photo = read(h).photo!;
    expect((photo.width, photo.height), (30, 40));
    final baked = img.decodeJpg(photo.bytes)!;
    expect((baked.width, baked.height), (30, 40));
    final px = baked.getPixel(27, 5);
    expect(px.r, greaterThan(200));
    expect(px.g, lessThan(60));

    // The displayed box has the baked aspect ratio, not the stored one.
    final box = t.getRect(photoBox);
    expect(box.width / box.height, closeTo(30 / 40, 0.01));

    await tapPhotoAt(t, 27 / 30, 5 / 40);
    final ghost = read(h).ghost!;
    expect(ghost.x, closeTo(0.9, 0.01));
    expect(ghost.y, closeTo(0.125, 0.01));
    // What was uploaded is what was shown.
    expect(h.uploader.uploads.single.length, photo.bytes.length);
  });

  testWidgets('publish_disabled_until_one_chapter', (t) async {
    final h = await pumpCreate(t);
    await pickAndAnnotate(t);
    final publish = find.byKey(const ValueKey('publish'));
    expect(
      t
          .widget<InkWell>(
            find.descendant(of: publish, matching: find.byType(InkWell)),
          )
          .onTap,
      isNull,
    );
    await t.tap(publish, warnIfMissed: false);
    await t.pump();
    expect(h.calls('POST', '/v1/works/w1/publish'), isEmpty);

    await addChapter(t, title: 'One');
    expect(
      t
          .widget<InkWell>(
            find.descendant(of: publish, matching: find.byType(InkWell)),
          )
          .onTap,
      isNotNull,
    );
  });

  testWidgets('publish_polls_until_published', (t) async {
    final h = await pumpCreate(
      t,
      statuses: const ['pending', 'pending', 'published'],
    );
    await pickAndAnnotate(t);
    await addChapter(t, title: 'One');
    await t.tap(find.byKey(const ValueKey('publish')));
    await t.pumpAndSettle();

    expect(h.calls('PUT', '/v1/works/w1'), hasLength(1));
    expect(h.calls('POST', '/v1/works/w1/publish'), hasLength(1));
    expect(h.calls('GET', '/v1/works/w1'), hasLength(3));
    expect(h.delays, const [Duration(seconds: 1), Duration(seconds: 2)]);
    expect(find.text('route:story/w1'), findsOneWidget);
  });

  testWidgets('blocked_status_shows_policy_message_and_review_note', (t) async {
    await pumpCreate(t, statuses: const ['pending', 'blocked']);
    await pickAndAnnotate(t);
    await addChapter(t, title: 'One');
    await t.tap(find.byKey(const ValueKey('publish')));
    await t.pumpAndSettle();
    expect(
      find.text(
        "This photo can't be published (content policy). It has been sent for review.",
      ),
      findsOneWidget,
    );
    expect(find.text('route:story/w1'), findsNothing);
  });

  testWidgets('polling_gives_up_with_still_processing_after_two_minutes', (
    t,
  ) async {
    final h = await pumpCreate(t, statuses: const ['pending']);
    await pickAndAnnotate(t);
    await addChapter(t, title: 'One');
    await t.tap(find.byKey(const ValueKey('publish')));
    await t.pumpAndSettle();
    final total = h.delays.fold(Duration.zero, (a, b) => a + b);
    expect(total, greaterThanOrEqualTo(const Duration(seconds: 100)));
    expect(total, lessThanOrEqualTo(const Duration(seconds: 130)));
    expect(h.delays.every((d) => d <= const Duration(seconds: 5)), isTrue);
    expect(find.textContaining('still processing'), findsOneWidget);
  });

  testWidgets('back_with_unsaved_pins_asks_to_discard', (t) async {
    final h = await pumpCreate(t);
    await pickAndAnnotate(t);
    await addChapter(t, title: 'One');

    await t.binding.handlePopRoute();
    await t.pumpAndSettle();
    expect(find.text('Discard this story?'), findsOneWidget);
    expect(find.text('route:feed'), findsNothing);

    await t.tap(find.byKey(const ValueKey('keep-editing')));
    await t.pumpAndSettle();
    expect(read(h).pins, hasLength(1));
    expect(h.calls('DELETE', '/v1/works/w1'), isEmpty);

    await t.binding.handlePopRoute();
    await t.pumpAndSettle();
    await t.tap(find.byKey(const ValueKey('confirm-discard')));
    await t.pumpAndSettle();
    expect(find.text('route:feed'), findsOneWidget);
    expect(h.calls('DELETE', '/v1/works/w1'), hasLength(1));
  });

  testWidgets('back_without_pins_leaves_and_frees_the_draft_silently', (
    t,
  ) async {
    final h = await pumpCreate(t);
    await pickAndAnnotate(t);
    await t.binding.handlePopRoute();
    await t.pumpAndSettle();
    expect(find.text('route:feed'), findsOneWidget);
    expect(h.calls('DELETE', '/v1/works/w1'), hasLength(1));
  });

  testWidgets('failed_save_keeps_pins_and_allows_retry', (t) async {
    final h = await pumpCreate(
      t,
      updateStatus: 400,
      updateCode: 'validation_failed',
    );
    await pickAndAnnotate(t);
    await addChapter(t, title: 'One');
    await t.tap(find.byKey(const ValueKey('publish')));
    await t.pumpAndSettle();
    expect(read(h).pins, hasLength(1));
    expect(read(h).phase, CreatePhase.annotate);
    expect(h.calls('POST', '/v1/works/w1/publish'), isEmpty);
    expect(find.byKey(const ValueKey('create-error')), findsOneWidget);
  });

  testWidgets('unsupported_file_type_is_refused_before_a_draft_is_created', (
    t,
  ) async {
    final h = await pumpCreate(t);
    h.picker.result = PickedPhoto(Uint8List.fromList(List.filled(64, 7)));
    await pickAndAnnotate(t);
    expect(find.text('Choose a JPEG, PNG or WebP photo.'), findsOneWidget);
    expect(h.calls('POST', '/v1/works'), isEmpty);
  });

  testWidgets('session_cleared_mid_flow_aborts_cleanly', (t) async {
    final h = await pumpCreate(t);
    await pickAndAnnotate(t);
    await addChapter(t, title: 'One');
    await h.container.read(sessionProvider.notifier).clear();
    await t.pumpAndSettle();
    expect(find.byKey(const ValueKey('create-screen')), findsNothing);
    expect(find.text('route:feed'), findsOneWidget);
  });
}
