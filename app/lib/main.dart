import 'package:flutter/material.dart';

import 'core/branding.dart';
import 'core/theme/app_theme.dart';

void main() => runApp(const VelaApp());

class VelaApp extends StatelessWidget {
  const VelaApp({super.key});

  @override
  Widget build(BuildContext context) {
    return MaterialApp(
      title: Branding.appName,
      theme: buildTheme(),
      home: const Scaffold(body: Center(child: Text(Branding.appName))),
    );
  }
}
