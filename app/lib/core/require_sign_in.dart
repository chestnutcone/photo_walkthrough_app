import 'package:flutter/widgets.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import 'auth/auth_controller.dart';

/// Opens the sign-in sheet and reports whether sign-in succeeded.
/// B8 overrides [signInSheetLauncherProvider] with the real sheet.
typedef SignInSheetLauncher = Future<bool> Function(BuildContext context);

final signInSheetLauncherProvider = Provider<SignInSheetLauncher>(
  (_) =>
      (_) async => false,
);

/// Call sites MUST run this before navigating to a gated route (`/create`,
/// `/notifications`, `/settings`); the router only bounces guests to `/`.
///
/// True when signed in; otherwise shows the sign-in sheet and returns its
/// outcome.
Future<bool> requireSignIn(BuildContext context, WidgetRef ref) async {
  final session = await ref.read(sessionProvider.future);
  if (session != null) return true;
  if (!context.mounted) return false;
  return ref.read(signInSheetLauncherProvider)(context);
}
