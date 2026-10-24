import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:vela_app/core/api/api_providers.dart';
import 'package:vela_app/core/auth/auth_controller.dart';
import 'package:vela_app/core/auth/session_store.dart';
import 'package:vela_app/core/config.dart';
import 'package:vela_app/core/require_sign_in.dart';
import 'package:vela_app/core/router.dart';
import 'package:vela_app/features/auth/suspended_screen.dart';
import 'package:vela_app/features/feed/feed_screen.dart';
import 'package:vela_app/features/onboarding/legal_links.dart';

import 'support/fakes.dart';

final _signedIn = Session(
  accessToken: 'A',
  refreshToken: 'R',
  accessExpiresAt: DateTime.utc(2030),
).encode();

class _Rig {
  _Rig(this.container, this.storage, this.launched, this.mailtos);
  final ProviderContainer container;
  final FakeSessionStorage storage;
  final List<String> launched;
  final List<Uri> mailtos;
}

Future<_Rig> _pump(
  WidgetTester tester, {
  String blockCode = 'account_suspended',
  Future<bool> Function(ProviderContainer)? signIn,
}) async {
  final storage = FakeSessionStorage(_signedIn);
  final launched = <String>[];
  final mailtos = <Uri>[];
  late ProviderContainer c;
  c = ProviderContainer(
    overrides: [
      sessionStorageProvider.overrideWithValue(storage),
      needsOnboardingProvider.overrideWithValue(false),
      httpClientAdapterProvider.overrideWithValue(
        FakeAdapter((o) {
          if (o.path == '/v1/me/ping') {
            return jsonResponse(403, errEnvelope(blockCode));
          }
          return jsonResponse(404, errEnvelope('not_found'));
        }),
      ),
      signInSheetLauncherProvider.overrideWithValue((_) async {
        launched.add('sheet');
        return signIn == null ? false : await signIn(c);
      }),
      legalUrlLauncherProvider.overrideWithValue((uri) async {
        mailtos.add(uri);
        return true;
      }),
    ],
  );
  addTearDown(c.dispose);
  await c.read(sessionProvider.future);
  await tester.pumpWidget(
    UncontrolledProviderScope(
      container: c,
      child: MaterialApp.router(routerConfig: c.read(routerProvider)),
    ),
  );
  await tester.pumpAndSettle();
  return _Rig(c, storage, launched, mailtos);
}

Future<void> _trigger(WidgetTester tester, _Rig rig) async {
  // The 403 surfaces as a DioException; the interceptor already acted on it.
  unawaited(
    rig.container
        .read(dioProvider)
        .get<dynamic>('/v1/me/ping')
        .then<void>((_) {}, onError: (Object _) {}),
  );
  await tester.pumpAndSettle();
}

void main() {
  testWidgets(
    'account_suspended_error_clears_session_and_shows_suspended_screen',
    (tester) async {
      final rig = await _pump(tester);
      expect(find.byType(FeedScreen), findsOneWidget);

      await _trigger(tester, rig);

      expect(find.byType(SuspendedScreen), findsOneWidget);
      expect(find.text('Your account is suspended'), findsOneWidget);
      expect(rig.storage.value, isNull);
      expect(rig.container.read(sessionProvider).value, isNull);
      expect(find.text(AppConfig.supportEmail), findsOneWidget);
    },
  );

  testWidgets('deleted_account_uses_different_wording', (tester) async {
    final rig = await _pump(tester, blockCode: 'account_deleted');
    await _trigger(tester, rig);

    expect(find.byType(SuspendedScreen), findsOneWidget);
    expect(find.text('This account has been deleted'), findsOneWidget);
    expect(find.text('Your account is suspended'), findsNothing);
  });

  testWidgets('contact_support_opens_a_mailto_without_account_data', (
    tester,
  ) async {
    final rig = await _pump(tester);
    await _trigger(tester, rig);

    await tester.tap(find.byKey(const ValueKey('suspended-support')));
    await tester.pumpAndSettle();

    final uri = rig.mailtos.single;
    expect(uri.scheme, 'mailto');
    expect(uri.path, AppConfig.supportEmail);
  });

  testWidgets('continue_as_guest_clears_the_block_and_returns_to_the_feed', (
    tester,
  ) async {
    final rig = await _pump(tester);
    await _trigger(tester, rig);

    await tester.tap(find.byKey(const ValueKey('suspended-guest')));
    await tester.pumpAndSettle();

    expect(rig.container.read(accountBlockProvider), isNull);
    expect(find.byType(SuspendedScreen), findsNothing);
    expect(find.byType(FeedScreen), findsOneWidget);
  });

  testWidgets('a_new_sign_in_clears_the_block', (tester) async {
    final rig = await _pump(
      tester,
      signIn: (c) async {
        await c
            .read(sessionProvider.notifier)
            .signIn(accessToken: 'B', refreshToken: 'RB', expiresIn: 900);
        return true;
      },
    );
    await _trigger(tester, rig);
    expect(find.byType(SuspendedScreen), findsOneWidget);

    await tester.tap(find.byKey(const ValueKey('suspended-sign-in')));
    await tester.pumpAndSettle();

    expect(rig.launched, ['sheet']);
    expect(rig.container.read(accountBlockProvider), isNull);
    expect(find.byType(SuspendedScreen), findsNothing);
    expect(find.byType(FeedScreen), findsOneWidget);
  });

  testWidgets('cancelling_the_sign_in_sheet_keeps_the_suspended_screen', (
    tester,
  ) async {
    final rig = await _pump(tester);
    await _trigger(tester, rig);

    await tester.tap(find.byKey(const ValueKey('suspended-sign-in')));
    await tester.pumpAndSettle();

    expect(find.byType(SuspendedScreen), findsOneWidget);
    expect(rig.container.read(accountBlockProvider), isNotNull);
  });

  testWidgets('other_routes_redirect_to_the_suspended_screen_without_looping', (
    tester,
  ) async {
    final rig = await _pump(tester);
    await _trigger(tester, rig);
    final router = rig.container.read(routerProvider);

    router.go('/search');
    await tester.pumpAndSettle();

    expect(find.byType(SuspendedScreen), findsOneWidget);
    expect(router.routerDelegate.currentConfiguration.uri.path, '/suspended');
  });

  test('resolveRedirect_sends_blocked_accounts_to_suspended_and_back', () {
    String? r(String loc, AccountBlock? b) => resolveRedirect(
      location: loc,
      isSignedIn: false,
      sessionLoading: false,
      needsOnboarding: false,
      accountBlock: b,
    );
    expect(r('/', AccountBlock.suspended), '/suspended');
    expect(r('/suspended', AccountBlock.deleted), isNull);
    expect(r('/suspended', null), '/');
    expect(r('/', null), isNull);
  });
}
