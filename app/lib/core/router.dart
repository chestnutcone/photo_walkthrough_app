import 'package:flutter/foundation.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import 'auth/auth_controller.dart';
import 'placeholder_screens.dart';

/// Set by the profile feature: true when the signed-in user has not finished
/// onboarding.
final needsOnboardingProvider = Provider<bool>((_) => false);

const _signInRequired = {'/create', '/notifications', '/settings'};

/// Pure redirect rule, exposed for testing.
String? resolveRedirect({
  required String location,
  required bool isSignedIn,
  required bool sessionLoading,
  required bool needsOnboarding,
}) {
  if (sessionLoading) return null;
  if (!isSignedIn) {
    return _signInRequired.contains(location) || location == '/onboarding'
        ? '/'
        : null;
  }
  if (needsOnboarding) return location == '/onboarding' ? null : '/onboarding';
  return location == '/onboarding' ? '/' : null;
}

final routerProvider = Provider<GoRouter>((ref) {
  final refresh = ValueNotifier<int>(0);
  void bump(Object? _, Object? _) => refresh.value++;
  ref.listen(sessionProvider, bump);
  ref.listen(needsOnboardingProvider, bump);
  ref.onDispose(refresh.dispose);

  final router = GoRouter(
    refreshListenable: refresh,
    redirect: (context, state) {
      final session = ref.read(sessionProvider);
      return resolveRedirect(
        location: state.uri.path,
        isSignedIn: session.value != null,
        sessionLoading: session.isLoading && !session.hasValue,
        needsOnboarding: ref.read(needsOnboardingProvider),
      );
    },
    routes: [
      GoRoute(path: '/', builder: (_, _) => const PlaceholderScreen('feed')),
      GoRoute(
        path: '/story/:id',
        builder: (_, s) => PlaceholderScreen('story/${s.pathParameters['id']}'),
      ),
      GoRoute(
        path: '/artist/:handle',
        builder: (_, s) =>
            PlaceholderScreen('artist/${s.pathParameters['handle']}'),
      ),
      GoRoute(
        path: '/create',
        builder: (_, _) => const PlaceholderScreen('create'),
      ),
      GoRoute(
        path: '/search',
        builder: (_, _) => const PlaceholderScreen('search'),
      ),
      GoRoute(
        path: '/notifications',
        builder: (_, _) => const PlaceholderScreen('notifications'),
      ),
      GoRoute(
        path: '/settings',
        builder: (_, _) => const PlaceholderScreen('settings'),
      ),
      GoRoute(
        path: '/onboarding',
        builder: (_, _) => const PlaceholderScreen('onboarding'),
      ),
    ],
  );
  ref.onDispose(router.dispose);
  return router;
});
