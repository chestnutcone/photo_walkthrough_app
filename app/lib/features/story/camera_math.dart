import 'package:flutter/animation.dart';
import 'package:flutter/foundation.dart';

/// Duration of every camera move in Story and Explore mode.
const Duration cameraDuration = Duration(milliseconds: 900);

/// Easing for every camera move: cubic-bezier(.2, .7, .1, 1).
const Cubic cameraCurve = Cubic(0.2, 0.7, 0.1, 1);

/// Vertical anchor of the pin inside the viewport (fraction of height).
const double _anchorY = 0.32;

/// Minimum camera scale. Below 1 the clamp bounds would invert.
const double _minScale = 1;

/// Camera transform for a pin, plus the on-screen spotlight position.
@immutable
class CameraState {
  const CameraState({
    required this.scale,
    required this.tx,
    required this.ty,
    required this.spotlight,
  });

  /// Identity camera. [spotlight] is unused and set to the origin.
  const CameraState.reset()
    : scale = 1,
      tx = 0,
      ty = 0,
      spotlight = Offset.zero;

  final double scale;
  final double tx;
  final double ty;
  final Offset spotlight;
}

/// Computes the camera for a pin at fractional position ([x], [y]).
///
/// A null [zoom] means "stay put": no scale, no translation, and the
/// spotlight sits on the pin. Otherwise the scale is `zoom * zoomMultiplier`
/// (clamped to at least 1) and the translation keeps the scaled image
/// covering the viewport.
CameraState computeCamera({
  required double x,
  required double y,
  required double? zoom,
  required double zoomMultiplier,
  required Size viewport,
}) {
  final w = viewport.width;
  final h = viewport.height;

  if (zoom == null) {
    return CameraState(
      scale: _minScale,
      tx: 0,
      ty: 0,
      spotlight: Offset(x * w, y * h),
    );
  }

  final s = _clampScale(zoom * zoomMultiplier);
  final tx = _clampTranslation(w / 2 - s * x * w, w - s * w);
  final ty = _clampTranslation(_anchorY * h - s * y * h, h - s * h);

  return CameraState(
    scale: s,
    tx: tx,
    ty: ty,
    spotlight: Offset(tx + s * x * w, ty + s * y * h),
  );
}

double _clampScale(double scale) => scale < _minScale ? _minScale : scale;

/// Clamps a translation to `[lowerBound, 0]`. With scale >= 1 the lower bound
/// is never above 0, so the range is always valid.
double _clampTranslation(double value, double lowerBound) {
  if (value < lowerBound) return lowerBound;
  if (value > 0) return 0;
  return value;
}
