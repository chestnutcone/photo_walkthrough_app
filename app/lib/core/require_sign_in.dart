import 'package:flutter/widgets.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../features/auth/sign_in_sheet.dart';
import 'auth/auth_controller.dart';
import 'auth/session_store.dart';

/// Opens the sign-in sheet and reports whether sign-in succeeded. The default
/// is the real sheet; tests override it.
typedef SignInSheetLauncher = Future<bool> Function(BuildContext context);

final signInSheetLauncherProvider = Provider<SignInSheetLauncher>(
  (_) => showSignInSheet,
);

/// Call sites MUST run this before navigating to a gated route (`/create`,
/// `/notifications`, `/settings`); the router only bounces guests to `/`.
///
/// True when signed in; otherwise shows the sign-in sheet and returns its
/// outcome. True means a session was stored. A brand-new account is sent to
/// onboarding by the router redirect at that moment, so the calling screen
/// may already be gone: callers check `context.mounted` and do not resume
/// their action until the user comes back from onboarding.
Future<bool> requireSignIn(BuildContext context, WidgetRef ref) =>
    requireSignInWith(
      context,
      session: () => ref.read(sessionProvider.future),
      launcher: () => ref.read(signInSheetLauncherProvider),
    );

/// [requireSignIn] for code that holds a provider `Ref` instead of a
/// `WidgetRef` (hooks built in providers).
Future<bool> requireSignInWith(
  BuildContext context, {
  required Future<Session?> Function() session,
  required SignInSheetLauncher Function() launcher,
}) async {
  if (await session() != null) return true;
  if (!context.mounted) return false;
  return launcher()(context);
}
