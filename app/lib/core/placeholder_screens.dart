import 'package:flutter/material.dart';

/// Stand-in for a routed screen; later tasks replace each use.
class PlaceholderScreen extends StatelessWidget {
  const PlaceholderScreen(this.name, {super.key});

  final String name;

  @override
  Widget build(BuildContext context) =>
      Scaffold(body: Center(child: Text('route:$name')));
}
