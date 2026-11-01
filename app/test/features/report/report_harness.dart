import 'dart:async';
import 'dart:convert';

import 'package:dio/dio.dart' show ResponseBody;
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:vela_app/core/api/api_providers.dart';
import 'package:vela_app/core/auth/session_store.dart';
import 'package:vela_app/core/require_sign_in.dart';
import 'package:vela_app/core/theme/app_theme.dart';
import 'package:vela_app/features/onboarding/legal_links.dart';
import 'package:vela_app/features/report/report_sheet_hook.dart';

import '../../core/support/fakes.dart';

class ReportRig {
  final adapterCalls = <RequestOptions0>[];
  final results = <bool>[];
  final openedLinks = <Uri>[];
  int signInLaunches = 0;
  late FakeAdapter adapter;
  List<Map<String, Object?>> get bodies => adapterCalls
      .map((r) => jsonDecode(r.body) as Map<String, Object?>)
      .toList();
}

class RequestOptions0 {
  RequestOptions0(this.path, this.body);
  final String path;
  final String body;
}

final _session = Session(
  accessToken: 'A',
  refreshToken: 'R',
  accessExpiresAt: DateTime.utc(2030),
).encode();

Future<ReportRig> pumpReport(
  WidgetTester tester, {
  bool signedIn = true,
  bool signInSucceeds = true,
  String targetType = 'reply',
  String targetId = '11111111-1111-4111-8111-111111111111',
  FutureOr<ResponseBody> Function()? respond,
}) async {
  tester.view.physicalSize = const Size(390, 844);
  tester.view.devicePixelRatio = 1;
  addTearDown(tester.view.reset);
  final rig = ReportRig();
  rig.adapter = FakeAdapter((o) {
    if (o.path == '/v1/reports') {
      rig.adapterCalls.add(
        RequestOptions0(
          o.path,
          o.data is String ? o.data as String : jsonEncode(o.data),
        ),
      );
      return respond != null
          ? respond()
          : jsonResponse(200, okEnvelope({'ok': true}));
    }
    return jsonResponse(404, errEnvelope('not_found'));
  });
  await tester.pumpWidget(
    ProviderScope(
      overrides: [
        sessionStorageProvider.overrideWithValue(
          FakeSessionStorage(signedIn ? _session : null),
        ),
        httpClientAdapterProvider.overrideWithValue(rig.adapter),
        signInSheetLauncherProvider.overrideWithValue((_) async {
          rig.signInLaunches++;
          return signInSucceeds;
        }),
        legalLinkOpenerProvider.overrideWithValue((ctx, uri) async {
          rig.openedLinks.add(uri);
        }),
      ],
      child: MaterialApp(
        theme: buildTheme(),
        home: Scaffold(
          body: Consumer(
            builder: (context, ref, _) => Center(
              child: TextButton(
                onPressed: () async {
                  final filed = await ref.read(reportSheetHook)(
                    context,
                    targetType: targetType,
                    targetId: targetId,
                  );
                  rig.results.add(filed);
                },
                child: const Text('open'),
              ),
            ),
          ),
        ),
      ),
    ),
  );
  await tester.tap(find.text('open'));
  await tester.pumpAndSettle();
  return rig;
}

Finder reasonTile(String value) => find.byKey(ValueKey('report-reason-$value'));
final sendButton = find.byKey(const ValueKey('report-send'));
final noteField = find.byKey(const ValueKey('report-note'));
final errorText = find.byKey(const ValueKey('report-error'));
