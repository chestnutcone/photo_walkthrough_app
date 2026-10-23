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
  static const input = Color(0xFF201E1B);

  /// rgba(242,238,230,.18): outline of pills such as Follow (README).
  static const outline = Color(0x2EF2EEE6);

  /// rgba(242,238,230,.08)
  static const hairline = Color(0x14F2EEE6);
}

/// Typography tokens. Families are declared in pubspec.yaml.
abstract final class VelaText {
  static const serifFamily = 'InstrumentSerif';
  static const sansFamily = 'Geist';
  static const monoFamily = 'GeistMono';
  static const families = [serifFamily, sansFamily, monoFamily];

  static TextStyle _serif(double size, double height) => TextStyle(
    fontFamily: serifFamily,
    fontSize: size,
    height: height,
    fontWeight: FontWeight.w400,
    color: VelaColors.ink,
  );

  static TextStyle _sans(
    double size, {
    double height = 1.3,
    FontWeight weight = FontWeight.w400,
  }) => TextStyle(
    fontFamily: sansFamily,
    fontSize: size,
    height: height,
    fontWeight: weight,
    color: VelaColors.ink,
  );

  static TextStyle _mono(double size, {FontWeight weight = FontWeight.w400}) =>
      TextStyle(
        fontFamily: monoFamily,
        fontSize: size,
        height: 1.3,
        fontWeight: weight,
        letterSpacing: 0.4,
        color: VelaColors.faint,
      );

  // Line heights marked (README) come from the handoff; the rest are our own
  // choices because the README gives none.
  static final serif46 = _serif(46, 0.95); // README
  static final serif44 = _serif(44, 1); // README
  static final serif40 = _serif(40, 1).copyWith(letterSpacing: -0.5); // README
  static final serif36 = _serif(36, 1); // README (prototype)
  static final serif34 = _serif(34, 1); // README
  static final serif32 = _serif(32, 1.02); // README
  static final serif30 = _serif(30, 1.05); // README (prototype)
  static final serif28 = _serif(28, 1.05); // README
  static final serif26 = _serif(26, 1.1); // chosen: not specified
  static final serif24 = _serif(24, 1.1); // chosen: not specified
  static final serif18 = _serif(18, 1.1); // chosen: not specified

  static final sans15 = _sans(15, height: 1.5); // README
  static final sans14 = _sans(14, height: 1.45); // README (reply text)
  static final sans13 = _sans(13); // chosen: 1.3
  static final sans12 = _sans(12); // chosen: 1.3
  static final sans11 = _sans(11); // chosen: 1.3

  // Weight variants (README: tabs, names, pills, buttons use 500; the
  // "Walk through" pill uses 600). Line height 1.3 chosen for single-line UI.
  static final sans15m = _sans(15, weight: FontWeight.w500);
  static final sans14m = _sans(14, weight: FontWeight.w500);
  static final sans13m = _sans(13, weight: FontWeight.w500);
  static final sans13s = _sans(13, weight: FontWeight.w600);
  static final sans12m = _sans(12, weight: FontWeight.w500);

  // Mono line height 1.3 chosen: not specified. Tracking .4 per README.
  static final mono12 = _mono(12);
  static final mono11 = _mono(11);
  static final mono10 = _mono(10);
  static final mono12m = _mono(12, weight: FontWeight.w500); // pin numbers

  /// Uppercase accent kicker ("CHAPTER 02 / 04"): mono 11, tracking .5.
  static final monoKicker = _mono(11)
      .copyWith(letterSpacing: 0.5, color: VelaColors.accent);

  static final List<TextStyle> all = [
    serif46,
    serif44,
    serif40,
    serif36,
    serif34,
    serif32,
    serif30,
    serif28,
    serif26,
    serif24,
    serif18,
    sans15,
    sans14,
    sans13,
    sans12,
    sans11,
    sans15m,
    sans14m,
    sans13m,
    sans13s,
    sans12m,
    mono12,
    mono11,
    mono10,
    mono12m,
    monoKicker,
  ];
}
