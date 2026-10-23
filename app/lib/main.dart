import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import 'core/branding.dart';
import 'core/router.dart';
import 'core/theme/app_theme.dart';
import 'features/search/recent_searches.dart';

void main() => runApp(const ProviderScope(child: VelaApp()));

class VelaApp extends ConsumerWidget {
  const VelaApp({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
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
