import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:vela_app/core/api/api_providers.dart';
import 'package:vela_app/core/auth/local_data.dart';
import 'package:vela_app/core/router.dart';
import 'package:vela_app/features/onboarding/legal_links.dart';
import 'package:vela_app/features/settings/delete_account_flow.dart';
import 'package:vela_app/features/settings/export_flow.dart';

import '../profile_edit/support/edit_harness.dart';

Future<void> _openDelete(WidgetTester tester) =>
    tapKey(tester, 'settings-delete');

void main() {
  const poll = Duration(seconds: 1);

  testWidgets('settings_shows_account_and_each_method_is_its_own_account', (
    tester,
  ) async {
    await pumpEdit(tester, path: '/settings');
    expect(find.text('Signed in as @ana.'), findsOneWidget);
    expect(find.textContaining('own account'), findsOneWidget);
    expect(find.textContaining('login-secret'), findsNothing);
  });

  testWidgets('sign_out_clears_session', (tester) async {
    final h = await pumpEdit(tester, path: '/settings');
    await tapKey(tester, 'settings-sign-out');
    expect(h.api.to('POST', '/v1/auth/logout'), hasLength(1));
    expect(h.storage.value, isNull);
    expect(h.container.read(sessionProvider).value, isNull);
    expect(h.location, '/', reason: 'guest lands on the feed');
  });

  group('delete account', () {
    testWidgets('delete_requires_typed_confirmation_and_clears_session', (
      tester,
    ) async {
      final wiped = <String>[];
      final h = await pumpEdit(tester, path: '/settings', overrides: []);
      h.container
          .read(localDataRegistryProvider)
          .register('cache', () async => wiped.add('cache'));
      await _openDelete(tester);
      expect(find.textContaining('permanently deletes'), findsOneWidget);
      expect(find.textContaining('30 days'), findsOneWidget);
      FilledButton confirm() => tester.widget(key('delete-confirm'));
      expect(confirm().onPressed, isNull);

      await tester.enterText(key('delete-confirm-field'), 'delete');
      await tester.pump();
      expect(confirm().onPressed, isNull, reason: 'case matters for the word');
      await tester.enterText(key('delete-confirm-field'), 'DELET');
      await tester.pump();
      expect(confirm().onPressed, isNull);
      expect(h.api.to('DELETE', '/v1/me'), isEmpty);

      await tester.enterText(key('delete-confirm-field'), 'DELETE');
      await tester.pump();
      expect(confirm().onPressed, isNotNull);
      await tester.tap(key('delete-confirm'));
      await settle(tester);

      expect(h.api.to('DELETE', '/v1/me'), hasLength(1));
      expect(h.storage.value, isNull);
      expect(wiped, ['cache'], reason: 'registered local data was wiped');
      expect(h.container.read(sessionProvider).value, isNull);
      expect(h.location, '/');
    });

    testWidgets('own_handle_also_confirms', (tester) async {
      await pumpEdit(tester, path: '/settings');
      await _openDelete(tester);
      await tester.enterText(key('delete-confirm-field'), 'ana');
      await tester.pump();
      expect(
        tester.widget<FilledButton>(key('delete-confirm')).onPressed,
        isNotNull,
      );
    });

    testWidgets('failure_keeps_session_and_shows_error', (tester) async {
      final h = await pumpEdit(
        tester,
        path: '/settings',
        api: EditApi(deleteStatus: 500),
      );
      await _openDelete(tester);
      await tester.enterText(key('delete-confirm-field'), 'DELETE');
      await tester.pump();
      await tester.tap(key('delete-confirm'));
      await settle(tester);
      expect(key('delete-error'), findsOneWidget);
      expect(h.storage.value, isNotNull);
      expect(h.container.read(sessionProvider).value, isNotNull);
      expect(h.location, '/settings');
    });

    Future<void> confirmDelete(WidgetTester tester) async {
      await _openDelete(tester);
      await tester.enterText(key('delete-confirm-field'), 'DELETE');
      await tester.pump();
      await tester.tap(key('delete-confirm'));
      await settle(tester);
    }

    testWidgets('account_already_deleted_counts_as_success', (tester) async {
      // The first request deleted the account but its response was lost;
      // the retry meets 403 account_deleted.
      final wiped = <String>[];
      final h = await pumpEdit(
        tester,
        path: '/settings',
        api: EditApi(deleteStatus: 403, deleteCode: 'account_deleted'),
      );
      h.container
          .read(localDataRegistryProvider)
          .register('cache', () async => wiped.add('cache'));
      await confirmDelete(tester);
      expect(wiped, ['cache']);
      expect(h.storage.value, isNull);
      expect(h.location, '/');
      expect(key('delete-error'), findsNothing);
    });

    testWidgets('unauthorized_after_refresh_counts_as_success', (tester) async {
      final h = await pumpEdit(
        tester,
        path: '/settings',
        api: EditApi(deleteStatus: 401, deleteCode: 'unauthorized'),
      );
      await confirmDelete(tester);
      expect(h.storage.value, isNull);
      expect(h.location, '/');
    });

    testWidgets('unexpected_error_never_leaves_the_dialog_busy', (
      tester,
    ) async {
      final h = await pumpEdit(
        tester,
        path: '/settings',
        overrides: [
          deleteAccountProvider.overrideWithValue(
            () async => throw StateError('boom'),
          ),
        ],
      );
      await confirmDelete(tester);
      expect(key('delete-error'), findsOneWidget);
      expect(
        tester.widget<FilledButton>(key('delete-confirm')).onPressed,
        isNotNull,
      );
      expect(h.container.read(sessionProvider).value, isNotNull);
    });

    testWidgets('double_tap_sends_one_request', (tester) async {
      final gate = Completer<void>();
      final h = await pumpEdit(
        tester,
        path: '/settings',
        api: EditApi()..deleteGate = gate,
      );
      await _openDelete(tester);
      await tester.enterText(key('delete-confirm-field'), 'DELETE');
      await tester.pump();
      await tester.tap(key('delete-confirm'));
      await tester.pump();
      expect(
        tester.widget<FilledButton>(key('delete-confirm')).onPressed,
        isNull,
      );
      await tester.tap(key('delete-confirm'), warnIfMissed: false);
      await tester.pump();
      gate.complete();
      await settle(tester);
      expect(h.api.to('DELETE', '/v1/me'), hasLength(1));
    });
  });

  group('export', () {
    testWidgets('export_shows_pending_then_download_link', (tester) async {
      final h = await pumpEdit(
        tester,
        path: '/settings',
        api: EditApi(exportStatuses: ['ready']),
        overrides: [exportPollIntervalProvider.overrideWithValue(poll)],
      );
      await tapKey(tester, 'export-request');
      expect(find.textContaining('Preparing your export'), findsOneWidget);
      expect(key('export-download'), findsNothing);

      await tester.pump(poll);
      await settle(tester);
      expect(find.textContaining('Your export is ready'), findsOneWidget);
      expect(
        find.textContaining('2026-10-30'),
        findsOneWidget,
        reason: 'expiry shown',
      );
      expect(h.launched, isEmpty);

      await tapKey(tester, 'export-download');
      expect(h.launched.single.host, 'blob.test');
      // The signed link is not kept anywhere on screen.
      expect(find.textContaining('SECRET'), findsNothing);
    });

    testWidgets('second_request_same_day_shows_server_message', (tester) async {
      final h = await pumpEdit(
        tester,
        path: '/settings',
        api: EditApi(exportRequestStatus: 429),
      );
      await tapKey(tester, 'export-request');
      expect(find.text('You can request one export per day.'), findsOneWidget);
      expect(h.api.to('POST', '/v1/me/export'), hasLength(1));
    });

    testWidgets('refresh_updates_a_pending_export', (tester) async {
      await pumpEdit(
        tester,
        path: '/settings',
        api: EditApi(exportStatuses: ['ready']),
        overrides: [
          exportPollIntervalProvider.overrideWithValue(
            const Duration(hours: 1),
          ),
        ],
      );
      await tapKey(tester, 'export-request');
      await tapKey(tester, 'export-refresh');
      expect(find.textContaining('Your export is ready'), findsOneWidget);
    });

    testWidgets('download_failure_is_reported', (tester) async {
      await pumpEdit(
        tester,
        path: '/settings',
        launchSucceeds: false,
        api: EditApi(exportStatuses: ['ready']),
        overrides: [
          exportPollIntervalProvider.overrideWithValue(
            const Duration(hours: 1),
          ),
        ],
      );
      await tapKey(tester, 'export-request');
      await tapKey(tester, 'export-refresh');
      await tapKey(tester, 'export-download');
      expect(key('export-message'), findsOneWidget);
    });
  });

  group('links', () {
    testWidgets('legal_links_open_the_right_pages', (tester) async {
      final opened = <Uri>[];
      await pumpEdit(
        tester,
        path: '/settings',
        overrides: [
          legalLinkOpenerProvider.overrideWithValue(
            (context, uri) async => opened.add(uri),
          ),
        ],
      );
      for (final id in [
        'link-privacy',
        'link-terms',
        'link-content',
        'link-dmca',
      ]) {
        await tapKey(tester, id);
      }
      expect(
        [for (final u in opened) u.pathSegments.last],
        ['privacy', 'terms', 'content', 'dmca'],
      );
      expect(opened.every((u) => u.path.startsWith('/legal/')), isTrue);
    });

    testWidgets('contact_support_opens_mailto', (tester) async {
      final h = await pumpEdit(tester, path: '/settings');
      await tapKey(tester, 'link-support');
      expect(h.launched.single.scheme, 'mailto');
      expect(h.launched.single.path, contains('@'));
    });

    testWidgets('version_is_shown', (tester) async {
      await pumpEdit(tester, path: '/settings');
      await tester.scrollUntilVisible(
        key('settings-version'),
        200,
        scrollable: find.byType(Scrollable).first,
      );
      expect(find.textContaining('Vela 1.0.0'), findsOneWidget);
    });
  });

  testWidgets('settings_and_edit_are_sign_in_gated', (tester) async {
    final h = await pumpEdit(tester, path: '/settings');
    await tapKey(tester, 'settings-sign-out');
    h.container.read(routerProvider).go('/settings');
    await settle(tester);
    expect(h.location, '/');
    h.container.read(routerProvider).go('/profile/edit');
    await settle(tester);
    expect(h.location, '/');
  });
}
