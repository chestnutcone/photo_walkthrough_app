import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:vela_api_client/vela_api_client.dart';
import 'package:vela_app/core/api/api_providers.dart';
import 'package:vela_app/core/auth/session_store.dart';
import 'package:vela_app/core/require_sign_in.dart';
import 'package:vela_app/core/theme/app_theme.dart';
import 'package:vela_app/features/replies/replies_sheet.dart';
import 'package:vela_app/features/report/report_sheet_hook.dart';

import '../../../core/support/fakes.dart';
import 'fake_replies_api.dart';

class SheetHarness {
  SheetHarness(this.api);
  final FakeRepliesApi api;
  final reports = <String>[];
  int signInLaunches = 0;
  int dismissed = 0;
}

final composerInput = find.byKey(const ValueKey('composer-input'));
final composerSend = find.byKey(const ValueKey('composer-send'));

/// Opens the sheet from a host button and settles.
Future<SheetHarness> pumpSheet(
  WidgetTester tester, {
  List<Reply>? replies,
  bool signedIn = true,
  bool readOnly = false,
  bool signInSucceeds = false,
  bool reportResult = false,
  FakeRepliesApi? fake,
  bool open = true,
}) async {
  tester.view.physicalSize = const Size(390, 844);
  tester.view.devicePixelRatio = 1;
  addTearDown(tester.view.reset);
  final api = fake ?? FakeRepliesApi();
  if (replies != null) api.autoList = replies;
  final h = SheetHarness(api);
  final session = signedIn
      ? Session(
          accessToken: 'A',
          refreshToken: 'R',
          accessExpiresAt: DateTime.utc(2030),
        ).encode()
      : null;
  await tester.pumpWidget(
    ProviderScope(
      overrides: [
        sessionStorageProvider.overrideWithValue(FakeSessionStorage(session)),
        apiProvider.overrideWithValue(api),
        signInSheetLauncherProvider.overrideWithValue((_) async {
          h.signInLaunches++;
          return signInSucceeds;
        }),
        reportSheetHook.overrideWithValue((
          ctx, {
          required targetType,
          required targetId,
        }) async {
          h.reports.add('$targetType/$targetId');
          return reportResult;
        }),
      ],
      child: MaterialApp(
        theme: buildTheme(),
        home: Builder(
          builder: (context) => Scaffold(
            body: Center(
              child: TextButton(
                onPressed: () async {
                  await showRepliesSheet(
                    context,
                    workId: 'w1',
                    chapterIndex: 0,
                    chapterTitle: 'The rope',
                    readOnly: readOnly,
                  );
                  h.dismissed++;
                },
                child: const Text('open'),
              ),
            ),
          ),
        ),
      ),
    ),
  );
  if (open) {
    await tester.tap(find.text('open'));
    await tester.pumpAndSettle();
  }
  return h;
}
