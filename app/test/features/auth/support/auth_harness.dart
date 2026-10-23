import 'dart:async';
import 'dart:convert';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_riverpod/misc.dart' show Override;
import 'package:flutter_test/flutter_test.dart';
import 'package:vela_app/core/api/api_providers.dart';
import 'package:vela_app/core/auth/session_store.dart';
import 'package:vela_app/core/theme/app_theme.dart';
import 'package:vela_app/features/auth/sign_in_sheet.dart';
import 'package:vela_app/features/auth/sso_client.dart';

import '../../../core/support/fakes.dart';

const phone = Size(390, 844);

/// Records calls; returns `idtoken-<provider>` unless told otherwise.
class FakeSso implements SsoClient {
  final List<({String provider, String nonce})> calls = [];
  Completer<String>? gate;
  Object? failWith;

  @override
  Future<String> idToken(String provider, String nonce) async {
    calls.add((provider: provider, nonce: nonce));
    if (failWith != null) throw failWith!;
    if (gate != null) return gate!.future;
    return 'idtoken-$provider';
  }
}

Map<String, Object?> meJson({bool onboarded = true}) => {
  'id': 'u1',
  'profile': null,
  'onboardingComplete': onboarded,
  'tier': {'name': 'free', 'maxWorks': 20},
  'usedWorks': 0,
};

/// Scripted auth API. [verifyResults] are consumed one per verify call; the
/// last one repeats.
class AuthApi {
  AuthApi({
    this.ssoStatus = 200,
    this.ssoCode = 'invalid_identity_token',
    this.requestStatus = 200,
    this.requestCode = 'rate_limited',
    this.requestRetryAfter,
    this.verifyResults = const [200],
    this.isNewUser = false,
    this.meOnboarded = true,
    this.meStatus = 200,
  });

  int ssoStatus;
  String ssoCode;
  int requestStatus;
  String requestCode;
  String? requestRetryAfter;
  List<int> verifyResults;
  bool isNewUser;
  bool meOnboarded;
  int meStatus;

  int verifyCount = 0;
  final List<RequestOptions> all = [];

  List<Map<String, dynamic>> bodies(String path) => [
    for (final r in all)
      if (r.path == path)
        Map<String, dynamic>.from(
          (r.data is String ? jsonDecode(r.data as String) : r.data) as Map,
        ),
  ];

  ResponseBody handle(RequestOptions o) {
    all.add(o);
    switch (o.path) {
      case '/v1/auth/sso':
        return ssoStatus == 200
            ? jsonResponse(200, okEnvelope(_pair()))
            : jsonResponse(ssoStatus, errEnvelope(ssoCode));
      case '/v1/auth/email/request':
        return requestStatus == 200
            ? jsonResponse(200, okEnvelope(null))
            : jsonResponse(
                requestStatus,
                errEnvelope(requestCode),
                headers: {
                  if (requestRetryAfter != null)
                    'retry-after': [requestRetryAfter!],
                },
              );
      case '/v1/auth/email/verify':
        final i = verifyCount < verifyResults.length
            ? verifyCount
            : verifyResults.length - 1;
        verifyCount++;
        final status = verifyResults[i];
        return status == 200
            ? jsonResponse(200, okEnvelope(_pair()))
            : jsonResponse(
                status,
                errEnvelope(status == 429 ? 'rate_limited' : 'invalid_code'),
                headers: {
                  if (status == 429) 'retry-after': ['90'],
                },
              );
      case '/v1/me':
        return meStatus != 200
            ? jsonResponse(meStatus, errEnvelope('server_error'))
            : jsonResponse(200, okEnvelope(meJson(onboarded: meOnboarded)));
    }
    return jsonResponse(404, errEnvelope('not_found'));
  }

  Map<String, Object?> _pair() => {
    'accessToken': 'A1',
    'refreshToken': 'R1',
    'expiresIn': 900,
    'isNewUser': isNewUser,
  };
}

class AuthHarness {
  AuthHarness({required this.storage, required this.api, required this.sso});
  final FakeSessionStorage storage;
  final AuthApi api;
  final FakeSso sso;
  bool? result;
  bool get stored => storage.value != null;
}

/// Pumps a page with a button that opens the sign-in sheet.
Future<AuthHarness> pumpSignIn(
  WidgetTester tester, {
  AuthApi? api,
  FakeSso? sso,
  bool apple = false,
  List<Override> overrides = const [],
  bool open = true,
}) async {
  tester.view.physicalSize = phone;
  tester.view.devicePixelRatio = 1;
  addTearDown(tester.view.reset);
  final h = AuthHarness(
    storage: FakeSessionStorage(),
    api: api ?? AuthApi(),
    sso: sso ?? FakeSso(),
  );
  await tester.pumpWidget(
    ProviderScope(
      overrides: [
        sessionStorageProvider.overrideWithValue(h.storage),
        httpClientAdapterProvider.overrideWithValue(FakeAdapter(h.api.handle)),
        ssoClientProvider.overrideWithValue(h.sso),
        appleSignInAvailableProvider.overrideWithValue(apple),
        ...overrides,
      ],
      child: MaterialApp(
        theme: buildTheme(),
        home: Builder(
          builder: (context) => Scaffold(
            body: Center(
              child: TextButton(
                key: const ValueKey('open'),
                onPressed: () async =>
                    h.result = await showSignInSheet(context),
                child: const Text('open'),
              ),
            ),
          ),
        ),
      ),
    ),
  );
  if (open) {
    await tester.tap(find.byKey(const ValueKey('open')));
    await tester.pumpAndSettle();
  }
  return h;
}

Finder key(String k) => find.byKey(ValueKey(k));

String jsonOf(Object o) => jsonEncode(o);
