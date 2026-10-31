// Shared harness for the on-device end-to-end tests. They run against the real
// local API (scripts/run-local-api.sh) and the host helper (tools/e2e_host_helper.py).
import 'dart:async';
import 'dart:convert';
import 'dart:io';
import 'dart:math';
import 'dart:typed_data';

import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';
import 'package:image/image.dart' as img;
import 'package:integration_test/integration_test.dart';
import 'package:shared_preferences/shared_preferences.dart';
import 'package:vela_app/core/attestation/attestation_client.dart';
import 'package:vela_app/core/attestation/platform_attestation.dart';
import 'package:vela_app/features/auth/sso_client.dart';
import 'package:vela_app/features/create/photo_picker.dart';
import 'package:vela_app/core/auth/session_store.dart';
import 'package:vela_app/main.dart';

const apiBase = 'http://10.0.2.2:5080';
const helperBase = 'http://10.0.2.2:5090';

/// Unique suffix so reruns never collide on handles or emails.
final String runId = (DateTime.now().millisecondsSinceEpoch % 100000000)
    .toRadixString(36);

IntegrationTestWidgetsFlutterBinding initBinding() =>
    IntegrationTestWidgetsFlutterBinding.ensureInitialized();

/// Fake photo picker handing out generated images.
class FakePhotoPicker implements PhotoPicker {
  FakePhotoPicker(this.next);
  Uint8List Function() next;
  @override
  Future<PickedPhoto?> pick(PhotoSource source) async => PickedPhoto(next());
}

/// A deterministic test photograph. [exifOrientation] 6 stores it rotated.
Uint8List testPhoto({int w = 1200, int h = 800, int? exifOrientation}) {
  final im = img.Image(width: w, height: h);
  final rnd = Random(w * 31 + h);
  for (var y = 0; y < h; y++) {
    for (var x = 0; x < w; x++) {
      im.setPixelRgb(x, y, (x * 255) ~/ w, (y * 255) ~/ h, 120);
    }
  }
  for (var i = 0; i < 8; i++) {
    img.fillCircle(
      im,
      x: rnd.nextInt(w),
      y: rnd.nextInt(h),
      radius: 40 + rnd.nextInt(120),
      color: img.ColorRgb8(rnd.nextInt(255), rnd.nextInt(255), rnd.nextInt(255)),
    );
  }
  if (exifOrientation != null) im.exif.imageIfd.orientation = exifOrientation;
  return Uint8List.fromList(img.encodeJpg(im, quality: 85));
}

int _launches = 0;

class _TestSso implements SsoClient {
  const _TestSso(this.subject);
  final String subject;
  @override
  Future<String> idToken(String provider, String nonce) async =>
      'fake:$subject:$subject@example.com';
}

/// Launches the real app on a clean install state.
Future<void> launchApp(
  WidgetTester tester, {
  FakePhotoPicker? picker,
  bool clean = true,
  Account? signedInAs,
  String? ssoSubject,
}) async {
  if (clean) {
    await const FlutterSecureStorage().deleteAll();
    (await SharedPreferences.getInstance()).clear();
  }
  if (signedInAs != null) {
    await const FlutterSecureStorage().write(
      key: 'vela.session',
      value: Session(
        accessToken: signedInAs.token,
        refreshToken: signedInAs.refresh,
        accessExpiresAt: DateTime.now().toUtc().add(const Duration(minutes: 14)),
      ).encode(),
    );
  }
  await tester.pumpWidget(
    ProviderScope(
      key: UniqueKey(),
      overrides: [
        attestationClientProvider.overrideWithValue(
          createPlatformAttestationClient(),
        ),
        if (picker != null) photoPickerProvider.overrideWithValue(picker),
        ssoClientProvider.overrideWithValue(
          _TestSso(ssoSubject ?? 'ui$runId${_launches++}'),
        ),
      ],
      child: const VelaApp(),
    ),
  );
  await settle(tester);
}

/// Pumps until [finder] matches or [timeout] passes (real network, so no
/// pumpAndSettle: spinners animate forever).
Future<void> pumpUntil(
  WidgetTester tester,
  Finder finder, {
  Duration timeout = const Duration(seconds: 40),
  String? reason,
}) async {
  final end = DateTime.now().add(timeout);
  while (DateTime.now().isBefore(end)) {
    await tester.pump(const Duration(milliseconds: 200));
    if (finder.evaluate().isNotEmpty) return;
  }
  fail('Timed out waiting for ${reason ?? finder.description}');
}

Future<void> pumpUntilGone(
  WidgetTester tester,
  Finder finder, {
  Duration timeout = const Duration(seconds: 40),
}) async {
  final end = DateTime.now().add(timeout);
  while (DateTime.now().isBefore(end)) {
    await tester.pump(const Duration(milliseconds: 200));
    if (finder.evaluate().isEmpty) return;
  }
  fail('Timed out waiting for ${finder.description} to disappear');
}

/// Lets animations and quick requests finish without waiting on spinners.
Future<void> settle(WidgetTester tester, [int ms = 1000]) async {
  for (var i = 0; i < ms ~/ 100; i++) {
    await tester.pump(const Duration(milliseconds: 100));
  }
}

Future<void> tapKey(WidgetTester tester, String key) async {
  final f = find.byKey(ValueKey(key));
  await pumpUntil(tester, f, reason: 'key $key');
  await tester.ensureVisible(f.first);
  await tester.tap(f.first);
  await settle(tester, 500);
}

Future<void> tapText(WidgetTester tester, String text) async {
  final f = find.text(text);
  await pumpUntil(tester, f, reason: 'text "$text"');
  await tester.ensureVisible(f.first);
  await tester.tap(f.first);
  await settle(tester, 500);
}

Future<void> enterKey(WidgetTester tester, String key, String text) async {
  final f = find.byKey(ValueKey(key));
  await pumpUntil(tester, f, reason: 'field $key');
  await tester.ensureVisible(f.first);
  await tester.enterText(f.first, text);
  await settle(tester, 300);
}

/// Marker the runner (scripts/run-e2e.sh) turns into an adb screenshot saved to
/// docs/review/screenshots/<flow>/<NN-name>.png.
Future<void> shot(WidgetTester tester, String flow, String name) async {
  await settle(tester, 700);
  // ignore: avoid_print
  print('E2E_SHOT $flow/$name');
  // Real wall-clock wait so the host can grab the frame.
  await tester.runAsync(() => Future<void>.delayed(const Duration(seconds: 2)));
  await tester.pump();
}

// ---- plain HTTP API access for second accounts and fixtures ----

class Account {
  Account(this.token, this.refresh, this.id, this.handle);
  final String token;
  final String refresh;
  final String id;
  final String handle;
}

Future<dynamic> http(
  String method,
  String url, {
  Object? body,
  String? token,
  Uint8List? raw,
  Map<String, String> headers = const {},
}) async {
  final c = HttpClient();
  try {
    final req = await c.openUrl(method, Uri.parse(url));
    if (token != null) req.headers.set('Authorization', 'Bearer $token');
    headers.forEach(req.headers.set);
    if (raw != null) {
      req.contentLength = raw.length;
      req.add(raw);
    } else if (body != null) {
      req.headers.contentType = ContentType.json;
      req.add(utf8.encode(jsonEncode(body)));
    }
    final res = await req.close();
    final text = await utf8.decodeStream(res);
    if (res.statusCode >= 400) {
      throw StateError('$method $url -> ${res.statusCode} $text');
    }
    return text.isEmpty ? null : jsonDecode(text);
  } finally {
    c.close(force: true);
  }
}

String get uniq => runId;

/// Signs a fresh fake-SSO account in over HTTP and onboards it.
Future<Account> makeAccount(String handle, {String? name}) async {
  final r = await http(
    'POST',
    '$apiBase/v1/auth/sso',
    body: {
      'provider': 'fake',
      'idToken': 'fake:$handle-$runId:$handle-$runId@example.com',
      'nonce': 'n',
    },
  );
  final token = r['data']['accessToken'] as String;
  final me = await http(
    'POST',
    '$apiBase/v1/me/onboarding',
    token: token,
    body: {
      'displayName': name ?? handle,
      'handle': handle,
      'ageConfirmed': true,
      'termsAccepted': true,
      'followArtistIds': <String>[],
    },
  );
  return Account(token, r['data']['refreshToken'] as String, me['data']['id'] as String, handle);
}

/// Creates and publishes a story over HTTP, returns the work id.
Future<String> publishStory(
  Account a,
  String title, {
  int chapters = 3,
}) async {
  final d = await http(
    'POST',
    '$apiBase/v1/works',
    token: a.token,
    body: {'contentType': 'image/jpeg', 'title': title},
  );
  final id = d['data']['workId'] as String;
  await http(
    'PUT',
    d['data']['uploadUrl'] as String,
    raw: testPhoto(),
    headers: {'x-ms-blob-type': 'BlockBlob', 'Content-Type': 'image/jpeg'},
  );
  await http(
    'PUT',
    '$apiBase/v1/works/$id',
    token: a.token,
    body: {
      'title': title,
      'intro': 'Intro for $title',
      'chapters': [
        for (var i = 0; i < chapters; i++)
          {
            'x': 0.2 + 0.2 * i,
            'y': 0.4,
            'zoom': i.isEven ? 2.4 : null,
            'title': 'Chapter ${i + 1}',
            'body': 'Body ${i + 1}',
          },
      ],
    },
  );
  await http('POST', '$apiBase/v1/works/$id/publish', token: a.token);
  for (var i = 0; i < 60; i++) {
    final w = await http('GET', '$apiBase/v1/works/$id', token: a.token);
    if (w['data']['status'] == 'published') return id;
    await Future<void>.delayed(const Duration(seconds: 1));
  }
  throw StateError('work $id never published');
}

Future<String> latestEmailCode() async =>
    (await _text('$helperBase/code')).trim();

Future<void> suspendHandle(String handle) async =>
    _text('$helperBase/suspend?handle=$handle');

Future<String> _text(String url) async {
  final c = HttpClient();
  try {
    final res = await (await c.getUrl(Uri.parse(url))).close();
    final t = await utf8.decodeStream(res);
    if (res.statusCode != 200) throw StateError('$url -> ${res.statusCode} $t');
    return t;
  } finally {
    c.close(force: true);
  }
}

/// Signs in through the app's sheet with the (fake) Google button and, for a
/// new account, completes onboarding with [handle].
Future<void> uiSignInGoogle(WidgetTester tester) async {
  await tapKey(tester, 'sso-google');
}

void log(String m) => debugPrint('E2E: $m');

/// Navigates the running app with its router (deterministic, no feed scrolling).
void goTo(WidgetTester tester, String location) {
  final ctx = tester.element(find.byType(Scaffold).first);
  GoRouter.of(ctx).go(location);
}
