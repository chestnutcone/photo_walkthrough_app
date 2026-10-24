import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import 'core/branding.dart';
import 'core/router.dart';
import 'core/theme/app_theme.dart';
import 'features/notifications/unread_count_provider.dart';
import 'features/search/recent_searches.dart';

void main() => runApp(const ProviderScope(child: VelaApp()));

class VelaApp extends ConsumerStatefulWidget {
  const VelaApp({super.key});

  @override
  ConsumerState<VelaApp> createState() => _VelaAppState();
}

class _VelaAppState extends ConsumerState<VelaApp> {
  late final AppLifecycleListener _lifecycle;

  @override
  void initState() {
    super.initState();
    // The bell badge is stale after the app was in the background.
    _lifecycle = AppLifecycleListener(
      onResume: () {
        if (mounted) ref.invalidate(unreadCountProvider);
      },
    );
  }

  @override
  void dispose() {
    _lifecycle.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    // Keeps recent searches alive from startup so their local-data cleaner is
    // registered before the user can delete the account.
    ref.listen(recentSearchesProvider, (_, _) {});
    return MaterialApp.router(
      title: Branding.appName,
      theme: buildTheme(),
      routerConfig: ref.watch(routerProvider),
    );
  }
}
