import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';

import 'theme/tokens.dart';

/// Stand-in for a routed screen; later tasks replace each use.
class PlaceholderScreen extends StatelessWidget {
  const PlaceholderScreen(this.name, {super.key});

  final String name;

  @override
  Widget build(BuildContext context) =>
      Scaffold(body: Center(child: Text('route:$name')));
}

/// Shown for unknown routes (for example a deep link to a path the app does
/// not know).
class NotFoundScreen extends StatelessWidget {
  const NotFoundScreen({super.key});

  @override
  Widget build(BuildContext context) => Scaffold(
    body: Center(
      child: Padding(
        padding: const EdgeInsets.symmetric(horizontal: 32),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          spacing: 12,
          children: [
            Text(
              "This page isn't available",
              textAlign: TextAlign.center,
              style: VelaText.serif28,
            ),
            Text(
              'The link may be old or mistyped.',
              textAlign: TextAlign.center,
              style: VelaText.sans15.copyWith(color: VelaColors.secondary),
            ),
            TextButton(
              onPressed: () => context.go('/'),
              child: const Text('Go home'),
            ),
          ],
        ),
      ),
    ),
  );
}
