import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:vela_app/core/branding.dart';
import 'package:vela_app/core/theme/app_theme.dart';
import 'package:vela_app/core/theme/tokens.dart';

const _serif = 'InstrumentSerif';
const _sans = 'Geist';
const _mono = 'GeistMono';

void expectStyle(
  TextStyle s, {
  required String family,
  required double size,
  required double height,
  FontWeight weight = FontWeight.w400,
  double? letterSpacing,
}) {
  expect(s.fontFamily, family);
  expect(s.fontSize, size);
  expect(s.height, height);
  expect(s.fontWeight, weight);
  expect(s.letterSpacing, letterSpacing);
}

void main() {
  TestWidgetsFlutterBinding.ensureInitialized();
  test('colors_match_handoff_tokens', () {
    expect(VelaColors.background, const Color(0xFF0F0E0D));
    expect(VelaColors.ink, const Color(0xFFF2EEE6));
    expect(VelaColors.secondary, const Color(0xFFD6D1C8));
    expect(VelaColors.muted, const Color(0xFFA39D93));
    expect(VelaColors.faint, const Color(0xFF8A847B));
    expect(VelaColors.accent, const Color(0xFFE5AE63));
    expect(VelaColors.sheet, const Color(0xFF161513));
    expect(VelaColors.input, const Color(0xFF201E1B));
    expect(VelaColors.hairline, const Color(0x14F2EEE6));
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

  test('theme_styles_divider_input_and_sheet_handle', () {
    final theme = buildTheme();
    expect(theme.dividerTheme.color, VelaColors.hairline);
    expect(theme.dividerTheme.thickness, 1);
    expect(theme.inputDecorationTheme.filled, isTrue);
    expect(theme.inputDecorationTheme.fillColor, VelaColors.input);
    final sheet = theme.bottomSheetTheme;
    expect(sheet.backgroundColor, VelaColors.sheet);
    expect(sheet.showDragHandle, isTrue);
    expect(sheet.dragHandleSize, const Size(36, 4));
    expect(sheet.dragHandleColor, VelaColors.faint);
  });

  test('serif_styles_match_handoff_scale', () {
    final expected = <TextStyle, List<double>>{
      VelaText.serif46: [46, 0.95],
      VelaText.serif44: [44, 1],
      VelaText.serif40: [40, 1],
      VelaText.serif36: [36, 1],
      VelaText.serif34: [34, 1],
      VelaText.serif32: [32, 1.02],
      VelaText.serif30: [30, 1.05],
      VelaText.serif28: [28, 1.05],
      VelaText.serif26: [26, 1.1],
      VelaText.serif24: [24, 1.1],
      VelaText.serif18: [18, 1.1],
    };
    expected.forEach((s, v) {
      expectStyle(
        s,
        family: _serif,
        size: v[0],
        height: v[1],
        letterSpacing: v[0] == 40 ? -0.5 : null,
      );
    });
  });

  test('sans_styles_and_weight_variants_match_handoff', () {
    expectStyle(VelaText.sans15, family: _sans, size: 15, height: 1.5);
    expectStyle(VelaText.sans14, family: _sans, size: 14, height: 1.45);
    expectStyle(VelaText.sans13, family: _sans, size: 13, height: 1.3);
    expectStyle(VelaText.sans12, family: _sans, size: 12, height: 1.3);
    expectStyle(VelaText.sans11, family: _sans, size: 11, height: 1.3);
    expectStyle(
      VelaText.sans15m,
      family: _sans,
      size: 15,
      height: 1.3,
      weight: FontWeight.w500,
    );
    expectStyle(
      VelaText.sans14m,
      family: _sans,
      size: 14,
      height: 1.3,
      weight: FontWeight.w500,
    );
    expectStyle(
      VelaText.sans13m,
      family: _sans,
      size: 13,
      height: 1.3,
      weight: FontWeight.w500,
    );
    expectStyle(
      VelaText.sans13s,
      family: _sans,
      size: 13,
      height: 1.3,
      weight: FontWeight.w600,
    );
    expectStyle(
      VelaText.sans12m,
      family: _sans,
      size: 12,
      height: 1.3,
      weight: FontWeight.w500,
    );
  });

  test('mono_styles_tracking_and_kicker_match_handoff', () {
    expectStyle(
      VelaText.mono12,
      family: _mono,
      size: 12,
      height: 1.3,
      letterSpacing: 0.4,
    );
    expectStyle(
      VelaText.mono11,
      family: _mono,
      size: 11,
      height: 1.3,
      letterSpacing: 0.4,
    );
    expectStyle(
      VelaText.mono10,
      family: _mono,
      size: 10,
      height: 1.3,
      letterSpacing: 0.4,
    );
    expectStyle(
      VelaText.mono12m,
      family: _mono,
      size: 12,
      height: 1.3,
      weight: FontWeight.w500,
      letterSpacing: 0.4,
    );
    expectStyle(
      VelaText.monoKicker,
      family: _mono,
      size: 11,
      height: 1.3,
      letterSpacing: 0.5,
    );
    expect(VelaText.monoKicker.color, VelaColors.accent);
  });

  test('every_text_style_family_is_bundled_exactly', () async {
    final manifest = jsonDecode(
      await rootBundle.loadString('FontManifest.json'),
    ) as List<dynamic>;
    final bundled = manifest.map((e) => (e as Map)['family'] as String).toSet();
    expect(VelaText.all, isNotEmpty);
    for (final style in VelaText.all) {
      expect(
        bundled.contains(style.fontFamily),
        isTrue,
        reason: '${style.fontFamily} not bundled',
      );
      expect([_serif, _sans, _mono].contains(style.fontFamily), isTrue);
    }
    expect(bundled, containsAll([_serif, _sans, _mono]));
  });

  test('branding_app_name_is_vela', () {
    expect(Branding.appName, 'Vela');
  });
}
