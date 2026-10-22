import 'package:flutter/material.dart';

import 'core/branding.dart';

void main() => runApp(const VelaApp());

class VelaApp extends StatelessWidget {
  const VelaApp({super.key});

  @override
  Widget build(BuildContext context) {
    return MaterialApp(
      title: Branding.appName,
      home: const Scaffold(body: Center(child: Text(Branding.appName))),
    );
  }
}
