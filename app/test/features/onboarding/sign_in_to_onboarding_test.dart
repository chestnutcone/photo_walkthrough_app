import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:vela_app/core/api/api_providers.dart';
import 'package:vela_app/core/auth/session_store.dart';
import 'package:vela_app/core/require_sign_in.dart';
import 'package:vela_app/core/router.dart';
import 'package:vela_app/core/theme/app_theme.dart';
import 'package:vela_app/features/auth/sso_client.dart';

import '../../core/support/fakes.dart';
import '../auth/support/auth_harness.dart' show AuthApi, FakeSso;
import 'support/onboarding_harness.dart';

/// Guest on the feed taps something that needs an account, signs in with a
/// brand-new Google account, and lands on onboarding with the sheet gone.
void main() {
  testWidgets('new_account_signing_in_from_the_feed_lands_on_onboarding', (
    tester,
  ) async {
    tester.view.physicalSize = phone;
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.reset);
    final auth = AuthApi(isNewUser: true, meOnboarded: false);
    final onboarding = OnboardingApi();
    final adapter = FakeAdapter(
      (o) => o.path.startsWith('/v1/auth/')
          ? auth.handle(o)
          : o.path == '/v1/me'
          ? auth.handle(o)
          : onboarding.handle(o),
    );
    bool? result;
    await tester.pumpWidget(
      ProviderScope(
        overrides: [
          sessionStorageProvider.overrideWithValue(FakeSessionStorage()),
          httpClientAdapterProvider.overrideWithValue(adapter),
          ssoClientProvider.overrideWithValue(FakeSso()),
          appleSignInAvailableProvider.overrideWithValue(false),
        ],
        child: Consumer(
          builder: (context, ref, _) => MaterialApp.router(
            theme: buildTheme(),
            routerConfig: ref.watch(routerProvider),
          ),
        ),
      ),
    );
    await settle(tester);
    final container = ProviderScope.containerOf(
      tester.element(find.byType(MaterialApp)),
    );
    final router = container.read(routerProvider);
    expect(router.routerDelegate.currentConfiguration.uri.path, '/');

    final context = tester.element(find.byType(Scaffold).first);
    final pending = container.read(signInSheetLauncherProvider)(context);
    await settle(tester);
    await tester.tap(find.byKey(const ValueKey('sso-google')));
    await settle(tester);
    result = await pending;

    expect(result, isTrue);
    expect(router.routerDelegate.currentConfiguration.uri.path, '/onboarding');
    expect(find.byKey(const ValueKey('sheet-close')), findsNothing);
    expect(find.byKey(const ValueKey('welcome-continue')), findsOneWidget);
  });
}
