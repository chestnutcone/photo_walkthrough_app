import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';

/// Stand-in for a routed screen; later tasks replace each use.
class PlaceholderScreen extends StatelessWidget {
  const PlaceholderScreen(this.name, {super.key});

  final String name;

  @override
  Widget build(BuildContext context) =>
      Scaffold(body: Center(child: Text('route:$name')));
}

/// Shown for unknown routes.
class NotFoundScreen extends StatelessWidget {
  const NotFoundScreen({super.key});

  @override
  Widget build(BuildContext context) => Scaffold(
    body: Center(
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          const Text('route:not-found'),
          TextButton(
            onPressed: () => context.go('/'),
            child: const Text('Go home'),
          ),
        ],
      ),
    ),
  );
}
