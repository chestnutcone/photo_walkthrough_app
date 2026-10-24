import 'package:flutter/foundation.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../features/auth/onboarding_flag.dart';
import 'auth/auth_controller.dart';
import '../features/create/create_screen.dart';
import '../features/feed/feed_screen.dart';
import '../features/notifications/notifications_screen.dart';
import '../features/onboarding/demo_story.dart';
import '../features/onboarding/onboarding_screen.dart';
import '../features/portfolio/portfolio_screen.dart';
import '../features/search/search_screen.dart';
import '../features/story/story_deeplink.dart';
import '../features/story/story_screen.dart';
import '../features/profile_edit/edit_profile_screen.dart';
import '../features/settings/settings_screen.dart';
import 'placeholder_screens.dart';

/// True when the signed-in account has not finished onboarding, from `/v1/me`
/// (with the sign-in response as a fallback). Tests override it.
final needsOnboardingProvider = Provider<bool>(deriveNeedsOnboarding);

const _signInRequired = [
  '/create',
  '/notifications',
  '/settings',
  '/profile/edit',
];

String _normalize(String path) => path.length > 1 && path.endsWith('/')
    ? path.substring(0, path.length - 1)
    : path;

/// `/onboarding` and its sample story at `/onboarding/demo`.
bool _isOnboarding(String path) =>
    path == '/onboarding' || path.startsWith('/onboarding/');

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
    return _isGated(location) || _isOnboarding(location) ? '/' : null;
  }
  if (needsOnboarding) return _isOnboarding(location) ? null : '/onboarding';
  return _isOnboarding(location) ? '/' : null;
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
        builder: (_, s) {
          final link = StoryDeepLink.fromQuery(s.uri.queryParameters);
          return StoryScreen(
            workId: s.pathParameters['id']!,
            initialChapter: link.chapter,
            openReplies: link.openReplies,
          );
        },
      ),
      GoRoute(
        path: '/artist/:handle',
        builder: (_, s) => PortfolioScreen(handle: s.pathParameters['handle']!),
      ),
      GoRoute(
        path: '/profile/edit',
        builder: (_, _) => const EditProfileScreen(),
      ),
      GoRoute(path: '/create', builder: (_, _) => const CreateScreen()),
      GoRoute(path: '/search', builder: (_, _) => const SearchScreen()),
      GoRoute(
        path: '/notifications',
        builder: (_, _) => const NotificationsScreen(),
      ),
      GoRoute(path: '/settings', builder: (_, _) => const SettingsScreen()),
      GoRoute(
        path: '/onboarding',
        builder: (_, _) => const OnboardingScreen(),
        routes: [
          GoRoute(path: 'demo', builder: (_, _) => const DemoStoryScreen()),
        ],
      ),
    ],
  );
  ref.onDispose(router.dispose);
  return router;
});
