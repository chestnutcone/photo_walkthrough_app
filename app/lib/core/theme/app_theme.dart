import 'package:flutter/material.dart';

import 'tokens.dart';

/// The single dark theme for the app. Screens should use [VelaText] styles
/// directly; the TextTheme mapping only covers stock Material widgets.
ThemeData buildTheme() {
  const scheme = ColorScheme.dark(
    primary: VelaColors.accent,
    onPrimary: VelaColors.background,
    secondary: VelaColors.accent,
    onSecondary: VelaColors.background,
    surface: VelaColors.background,
    onSurface: VelaColors.ink,
    surfaceContainer: VelaColors.sheet,
    surfaceContainerHigh: VelaColors.sheet,
    onSurfaceVariant: VelaColors.muted,
    outline: VelaColors.faint,
  );
  final text = TextTheme(
    displayLarge: VelaText.serif46,
    displayMedium: VelaText.serif40,
    displaySmall: VelaText.serif34,
    headlineLarge: VelaText.serif32,
    headlineMedium: VelaText.serif28,
    headlineSmall: VelaText.serif24,
    titleLarge: VelaText.serif26,
    titleMedium: VelaText.sans15,
    titleSmall: VelaText.sans14,
    bodyLarge: VelaText.sans15,
    bodyMedium: VelaText.sans14,
    bodySmall: VelaText.sans13,
    labelLarge: VelaText.sans13,
    labelMedium: VelaText.sans12,
    labelSmall: VelaText.mono11,
  );
  return ThemeData(
    useMaterial3: true,
    brightness: Brightness.dark,
    colorScheme: scheme,
    scaffoldBackgroundColor: VelaColors.background,
    canvasColor: VelaColors.background,
    fontFamily: VelaText.sansFamily,
    textTheme: text,
    dividerTheme: const DividerThemeData(
      color: VelaColors.hairline,
      thickness: 1,
      space: 1,
    ),
    inputDecorationTheme: const InputDecorationThemeData(
      filled: true,
      fillColor: VelaColors.input,
      hintStyle: TextStyle(color: VelaColors.faint),
    ),
    bottomSheetTheme: const BottomSheetThemeData(
      backgroundColor: VelaColors.sheet,
      showDragHandle: true,
      dragHandleSize: Size(36, 4),
      dragHandleColor: VelaColors.faint,
      shape: RoundedRectangleBorder(
        borderRadius: BorderRadius.vertical(top: Radius.circular(26)),
      ),
    ),
  );
}
