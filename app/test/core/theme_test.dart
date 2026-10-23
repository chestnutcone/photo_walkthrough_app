import 'dart:io';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:vela_app/core/branding.dart';
import 'package:vela_app/core/theme/app_theme.dart';
import 'package:vela_app/core/theme/tokens.dart';

void main() {
  test('colors_match_handoff_tokens', () {
    expect(VelaColors.background, const Color(0xFF0F0E0D));
    expect(VelaColors.ink, const Color(0xFFF2EEE6));
    expect(VelaColors.secondary, const Color(0xFFD6D1C8));
    expect(VelaColors.muted, const Color(0xFFA39D93));
    expect(VelaColors.faint, const Color(0xFF8A847B));
    expect(VelaColors.accent, const Color(0xFFE5AE63));
    expect(VelaColors.sheet, const Color(0xFF161513));
  });

  test('theme_uses_background_and_ink', () {
    final theme = buildTheme();
    expect(theme.scaffoldBackgroundColor, VelaColors.background);
    expect(theme.colorScheme.surface, VelaColors.background);
    expect(theme.colorScheme.onSurface, VelaColors.ink);
    expect(theme.colorScheme.primary, VelaColors.accent);
    expect(theme.brightness, Brightness.dark);
    expect(theme.textTheme.bodyMedium?.color, VelaColors.ink);
  });

  test('all_text_styles_use_bundled_font_families', () {
    expect(VelaText.serifSizes, [46, 40, 34, 32, 28, 26, 24, 18]);
    expect(VelaText.sansSizes, [15, 14, 13, 12, 11]);
    expect(VelaText.monoSizes, [12, 11, 10]);
    final pubspec = File('pubspec.yaml').readAsStringSync();
    for (final style in VelaText.all) {
      expect(style.fontFamily, isIn(VelaText.families));
      expect(pubspec, contains('family: ${style.fontFamily}'));
    }
    expect(VelaText.serif46.fontSize, 46);
    expect(VelaText.mono10.fontSize, 10);
    expect(VelaText.sans15.height, 1.5);
    expect(Branding.appName, 'Vela');
  });
}
