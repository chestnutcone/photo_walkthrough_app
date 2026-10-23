import 'package:flutter/material.dart';

import 'tokens.dart';

/// Filled ink button with dark text (README: "Walk through" pill colours).
/// 48pt tall, so the target is above the 44pt minimum.
ButtonStyle velaFilledButtonStyle() => FilledButton.styleFrom(
  backgroundColor: VelaColors.ink,
  foregroundColor: VelaColors.background,
  disabledBackgroundColor: VelaColors.input,
  disabledForegroundColor: VelaColors.faint,
  minimumSize: const Size(44, 48),
  padding: const EdgeInsets.symmetric(horizontal: 20),
  shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(12)),
  textStyle: VelaText.sans15m,
);

/// 1px outline button (Follow pill outline colour).
ButtonStyle velaOutlinedButtonStyle() => OutlinedButton.styleFrom(
  foregroundColor: VelaColors.ink,
  minimumSize: const Size(44, 52),
  padding: const EdgeInsets.symmetric(horizontal: 20),
  side: const BorderSide(color: VelaColors.outline),
  shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(12)),
  textStyle: VelaText.sans15m,
);

ButtonStyle velaTextButtonStyle() => TextButton.styleFrom(
  foregroundColor: VelaColors.secondary,
  minimumSize: const Size(44, 44),
  textStyle: VelaText.sans13m,
);

/// Input style from the handoff: `#201E1B` fill, radius 10, no border.
InputDecoration velaInputDecoration({
  String? hint,
  String? errorText,
  String? helperText,
  Widget? suffix,
}) {
  final border = OutlineInputBorder(
    borderRadius: BorderRadius.circular(10),
    borderSide: BorderSide.none,
  );
  return InputDecoration(
    hintText: hint,
    errorText: errorText,
    helperText: helperText,
    helperMaxLines: 3,
    errorMaxLines: 3,
    suffixIcon: suffix,
    filled: true,
    fillColor: VelaColors.input,
    hintStyle: VelaText.sans15.copyWith(color: VelaColors.faint),
    errorStyle: VelaText.sans12.copyWith(color: VelaColors.danger),
    helperStyle: VelaText.sans12.copyWith(color: VelaColors.muted),
    contentPadding: const EdgeInsets.symmetric(horizontal: 14, vertical: 14),
    border: border,
    enabledBorder: border,
    focusedBorder: border.copyWith(
      borderSide: const BorderSide(color: VelaColors.accent, width: 1.5),
    ),
    errorBorder: border.copyWith(
      borderSide: const BorderSide(color: VelaColors.danger),
    ),
    focusedErrorBorder: border.copyWith(
      borderSide: const BorderSide(color: VelaColors.danger, width: 1.5),
    ),
  );
}
