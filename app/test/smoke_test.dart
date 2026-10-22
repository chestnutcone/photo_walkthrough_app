import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:vela_app/main.dart';

void main() {
  testWidgets('VelaApp builds a MaterialApp', (tester) async {
    await tester.pumpWidget(const VelaApp());
    expect(find.byType(MaterialApp), findsOneWidget);
  });
}
