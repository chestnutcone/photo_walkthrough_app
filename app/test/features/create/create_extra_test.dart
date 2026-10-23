import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:vela_app/core/api/api_providers.dart';
import 'package:vela_app/features/create/blob_uploader.dart';
import 'package:vela_app/features/create/create_controller.dart';
import 'package:vela_app/features/create/create_state.dart';

import 'support/create_harness.dart';

CreateState read(CreateHarness h) => h.container.read(createControllerProvider);

void main() {
  testWidgets('tapping_a_chapter_edits_it_in_place_keeping_order', (t) async {
    final h = await pumpCreate(t);
    await pickAndAnnotate(t);
    await addChapter(t, title: 'A', fx: 0.2, fy: 0.2);
    await addChapter(t, title: 'B', fx: 0.8, fy: 0.8);

    await t.ensureVisible(find.byKey(const ValueKey('chapter-row-0')));
    await t.tap(find.byKey(const ValueKey('chapter-row-0')));
    await t.pumpAndSettle();
    expect(read(h).editingIndex, 0);
    await t.enterText(find.byKey(const ValueKey('chapter-title')), 'A2');
    await t.pump();
    await t.ensureVisible(find.byKey(const ValueKey('add-chapter')));
    await t.tap(find.byKey(const ValueKey('add-chapter')));
    await t.pumpAndSettle();
    expect(read(h).pins.map((p) => p.title), ['A2', 'B']);
    expect(read(h).editingIndex, isNull);
  });

  testWidgets('removing_a_chapter_renumbers_the_rest', (t) async {
    final h = await pumpCreate(t);
    await pickAndAnnotate(t);
    await addChapter(t, title: 'A', fx: 0.2, fy: 0.2);
    await addChapter(t, title: 'B', fx: 0.8, fy: 0.8);
    await t.ensureVisible(find.byKey(const ValueKey('remove-0')));
    await t.tap(find.byKey(const ValueKey('remove-0')));
    await t.pumpAndSettle();
    expect(read(h).pins.map((p) => p.title), ['B']);
    expect(find.text('1 CHAPTER'), findsOneWidget);
  });

  testWidgets(
    'expired_draft_keeps_pins_and_publishing_again_starts_a_new_one',
    (t) async {
      final h = await pumpCreate(
        t,
        publishStatus: 409,
        publishCode: 'draft_expired',
      );
      await pickAndAnnotate(t);
      await addChapter(t, title: 'One');
      await t.tap(find.byKey(const ValueKey('publish')));
      await t.pumpAndSettle();
      expect(read(h).pins, hasLength(1));
      expect(read(h).workId, isNull);
      expect(read(h).phase, CreatePhase.annotate);

      await t.tap(find.byKey(const ValueKey('publish')));
      await t.pumpAndSettle();
      expect(h.calls('POST', '/v1/works'), hasLength(2));
      expect(h.uploader.uploads, hasLength(2));
    },
  );

  testWidgets('failed_upload_offers_retry_and_blocks_publish', (t) async {
    final h = await pumpCreate(t);
    h.uploader.failWith = const BlobUploadException();
    await pickAndAnnotate(t);
    expect(read(h).uploadFailed, isTrue);
    expect(find.byKey(const ValueKey('retry-upload')), findsOneWidget);

    h.uploader.failWith = null;
    await t.tap(find.byKey(const ValueKey('retry-upload')));
    await t.pumpAndSettle();
    expect(read(h).uploadFailed, isFalse);
    expect(h.uploader.uploads, hasLength(1));
    expect(h.calls('POST', '/v1/works'), hasLength(1), reason: 'same draft');
  });

  testWidgets('leaving_stops_polling_and_keeps_the_pending_story', (t) async {
    final gate = Completer<void>();
    final h = await pumpCreate(
      t,
      statuses: const ['pending'],
      pollDelay: (_) => gate.future,
    );
    await pickAndAnnotate(t);
    await addChapter(t, title: 'One');
    await t.tap(find.byKey(const ValueKey('publish')));
    for (var i = 0; i < 10; i++) {
      await t.pump(const Duration(milliseconds: 50));
    }
    final before = h.calls('GET', '/v1/works/w1').length;
    expect(before, 1);

    await t.tap(find.byKey(const ValueKey('cancel')));
    await t.pumpAndSettle();
    gate.complete();
    await t.pumpAndSettle();
    expect(h.calls('GET', '/v1/works/w1'), hasLength(before));
    expect(h.calls('DELETE', '/v1/works/w1'), isEmpty);
  });

  testWidgets('session_change_during_polling_cancels_it', (t) async {
    final gate = Completer<void>();
    final h = await pumpCreate(
      t,
      statuses: const ['pending'],
      pollDelay: (_) => gate.future,
    );
    await pickAndAnnotate(t);
    await addChapter(t, title: 'One');
    await t.tap(find.byKey(const ValueKey('publish')));
    for (var i = 0; i < 10; i++) {
      await t.pump(const Duration(milliseconds: 50));
    }
    await h.container.read(sessionProvider.notifier).clear();
    gate.complete();
    await t.pumpAndSettle();
    expect(h.calls('GET', '/v1/works/w1'), hasLength(1));
    expect(find.text('route:feed'), findsOneWidget);
  });
}
