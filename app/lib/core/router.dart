import 'package:flutter/foundation.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import 'auth/auth_controller.dart';
import '../features/feed/feed_screen.dart';
import '../features/portfolio/portfolio_screen.dart';
import '../features/story/story_screen.dart';
import 'placeholder_screens.dart';

/// Set by the profile feature: true when the signed-in user has not finished
/// onboarding.
final needsOnboardingProvider = Provider<bool>((_) => false);

const _signInRequired = [
  '/create',
  '/notifications',
  '/settings',
  '/profile/edit',
];

String _normalize(String path) => path.length > 1 && path.endsWith('/')
    ? path.substring(0, path.length - 1)
    : path;

bool _isGated(String path) =>
    _signInRequired.any((base) => path == base || path.startsWith('$base/'));

/// Pure redirect rule, exposed for testing.
String? resolveRedirect({
  required String location,
  required bool isSignedIn,
  required bool sessionLoading,
  required bool needsOnboarding,
}) {
  location = _normalize(location);
  if (sessionLoading) return null;
  if (!isSignedIn) {
    return _isGated(location) || location == '/onboarding' ? '/' : null;
  }
  if (needsOnboarding) return location == '/onboarding' ? null : '/onboarding';
  return location == '/onboarding' ? '/' : null;
}

/// Guests who deep-link to a gated route are sent to `/`, because a redirect
/// cannot open the sign-in sheet. Call sites MUST run `requireSignIn` before
/// navigating to `/create`, `/notifications`, `/settings` (or sub-routes).
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
    errorBuilder: (context, state) => const NotFoundScreen(),
    routes: [
      GoRoute(path: '/', builder: (_, _) => const FeedScreen()),
      GoRoute(
        path: '/story/:id',
        builder: (_, s) => StoryScreen(workId: s.pathParameters['id']!),
      ),
      GoRoute(
        path: '/artist/:handle',
        builder: (_, s) => PortfolioScreen(handle: s.pathParameters['handle']!),
      ),
      GoRoute(
        path: '/profile/edit',
        builder: (_, _) => const PlaceholderScreen('profile/edit'),
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
