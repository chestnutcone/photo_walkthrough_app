import 'dart:ui' show lerpDouble;

import 'package:flutter/animation.dart';

import 'camera_math.dart';

/// Interpolates every camera field, so the photo transform and the spotlight
/// ring move together.
class CameraTween extends Tween<CameraState> {
  CameraTween({super.begin, super.end});

  @override
  CameraState lerp(double t) {
    final a = begin ?? end!;
    final b = end!;
    return CameraState(
      scale: lerpDouble(a.scale, b.scale, t)!,
      tx: lerpDouble(a.tx, b.tx, t)!,
      ty: lerpDouble(a.ty, b.ty, t)!,
      spotlight: Offset.lerp(a.spotlight, b.spotlight, t)!,
    );
  }
}
