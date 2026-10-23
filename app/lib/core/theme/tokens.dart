import 'package:flutter/material.dart';

/// Colour tokens from the design handoff README ("Design Tokens").
abstract final class VelaColors {
  static const background = Color(0xFF0F0E0D);
  static const ink = Color(0xFFF2EEE6);
  static const secondary = Color(0xFFD6D1C8);
  static const muted = Color(0xFFA39D93);
  static const faint = Color(0xFF8A847B);
  static const accent = Color(0xFFE5AE63);
  static const sheet = Color(0xFF161513);
}

/// Typography tokens. Families are declared in pubspec.yaml.
abstract final class VelaText {
  static const serifFamily = 'InstrumentSerif';
  static const sansFamily = 'Geist';
  static const monoFamily = 'GeistMono';
  static const families = [serifFamily, sansFamily, monoFamily];

  static const serifSizes = [46, 40, 34, 32, 28, 26, 24, 18];
  static const sansSizes = [15, 14, 13, 12, 11];
  static const monoSizes = [12, 11, 10];

  static TextStyle _serif(double size, double height) => TextStyle(
    fontFamily: serifFamily,
    fontSize: size,
    height: height,
    fontWeight: FontWeight.w400,
    color: VelaColors.ink,
  );

  static TextStyle _sans(double size, {double height = 1.3}) => TextStyle(
    fontFamily: sansFamily,
    fontSize: size,
    height: height,
    fontWeight: FontWeight.w400,
    color: VelaColors.ink,
  );

  static TextStyle _mono(double size) => TextStyle(
    fontFamily: monoFamily,
    fontSize: size,
    height: 1.3,
    fontWeight: FontWeight.w400,
    letterSpacing: 0.4,
    color: VelaColors.faint,
  );

  // Line heights follow the handoff where given (34/1, 32/1.02, 28/1.05, 46/.95).
  static final serif46 = _serif(46, 0.95);
  static final serif40 = _serif(40, 1).copyWith(letterSpacing: -0.5);
  static final serif34 = _serif(34, 1);
  static final serif32 = _serif(32, 1.02);
  static final serif28 = _serif(28, 1.05);
  static final serif26 = _serif(26, 1.1);
  static final serif24 = _serif(24, 1.1);
  static final serif18 = _serif(18, 1.1);

  static final sans15 = _sans(15, height: 1.5);
  static final sans14 = _sans(14, height: 1.45);
  static final sans13 = _sans(13);
  static final sans12 = _sans(12);
  static final sans11 = _sans(11);

  static final mono12 = _mono(12);
  static final mono11 = _mono(11);
  static final mono10 = _mono(10);

  static final List<TextStyle> all = [
    serif46,
    serif40,
    serif34,
    serif32,
    serif28,
    serif26,
    serif24,
    serif18,
    sans15,
    sans14,
    sans13,
    sans12,
    sans11,
    mono12,
    mono11,
    mono10,
  ];
}
