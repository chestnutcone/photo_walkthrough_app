import 'dart:math' as math;
import 'dart:ui' show ImageFilter;

import 'package:flutter/material.dart';

import '../../../core/theme/tokens.dart';
import '../camera_math.dart';
import '../motion.dart';

/// Hit area of a pin on screen.
const double pinHitSize = 44;
const double _badgeSize = 28;

/// A chapter pin: its fractional position in the camera layer and a label for
/// assistive tech.
class PinSpec {
  const PinSpec({required this.layerFraction, required this.label});

  final Offset layerFraction;
  final String label;
}

/// Numbered pins on the camera layer, counter-scaled by 1/scale so they keep
/// a constant on-screen size.
///
/// A pin whose true position is on screen is pulled into the safe rectangle:
/// at least 22pt from every screen edge (so the 44pt hit area stays on screen)
/// and above [safeBottom] (the top of the bottom card, so the card never
/// covers a pin). Tradeoff: such a pin's badge can sit up to 22pt (edges) or
/// more (under the card) away from its true spot, so in Explore the spotlight
/// ring and the badge of an edge pin can be offset by that much. Pins whose
/// true position is off screen (zoomed in) are left where they are.
class PinLayer extends StatelessWidget {
  const PinLayer({
    super.key,
    required this.pins,
    required this.viewport,
    required this.camera,
    required this.safeBottom,
    required this.visible,
    required this.onTap,
  });

  final List<PinSpec> pins;
  final Size viewport;
  final CameraState camera;
  final double safeBottom;
  final bool visible;
  final ValueChanged<int> onTap;

  /// Layer-space centre for the pin at [fraction].
  Offset _centre(Offset fraction) {
    const half = pinHitSize / 2;
    final s = camera.scale;
    final layer = Offset(
      fraction.dx * viewport.width,
      fraction.dy * viewport.height,
    );
    final screen = Offset(camera.tx + s * layer.dx, camera.ty + s * layer.dy);
    final onScreen =
        screen.dx >= 0 &&
        screen.dx <= viewport.width &&
        screen.dy >= 0 &&
        screen.dy <= viewport.height;
    if (!onScreen) return layer;
    final maxY = math.max(half, math.min(viewport.height, safeBottom) - half);
    final safe = Offset(
      screen.dx.clamp(half, viewport.width - half).toDouble(),
      screen.dy.clamp(half, maxY).toDouble(),
    );
    return Offset((safe.dx - camera.tx) / s, (safe.dy - camera.ty) / s);
  }

  @override
  Widget build(BuildContext context) {
    const half = pinHitSize / 2;
    return IgnorePointer(
      ignoring: !visible,
      child: ExcludeSemantics(
        excluding: !visible,
        child: AnimatedOpacity(
          key: const ValueKey('pin-layer'),
          opacity: visible ? 1 : 0,
          duration: motionDuration(context, const Duration(milliseconds: 400)),
          child: Stack(
            children: [
              for (var i = 0; i < pins.length; i++)
                Builder(
                  builder: (context) {
                    final c = _centre(pins[i].layerFraction);
                    return Positioned(
                      left: c.dx - half,
                      top: c.dy - half,
                      width: pinHitSize,
                      height: pinHitSize,
                      child: Transform.scale(
                        scale: 1 / camera.scale,
                        child: _Pin(
                          key: ValueKey('pin-$i'),
                          number: i + 1,
                          label: pins[i].label,
                          onTap: () => onTap(i),
                        ),
                      ),
                    );
                  },
                ),
            ],
          ),
        ),
      ),
    );
  }
}

class _Pin extends StatelessWidget {
  const _Pin({
    super.key,
    required this.number,
    required this.label,
    required this.onTap,
  });

  final int number;
  final String label;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) => Semantics(
    button: true,
    label: label,
    excludeSemantics: true,
    onTap: onTap,
    child: GestureDetector(
      behavior: HitTestBehavior.opaque,
      onTap: onTap,
      child: SizedBox(
        width: pinHitSize,
        height: pinHitSize,
        child: Center(
          child: ClipOval(
            child: BackdropFilter(
              filter: ImageFilter.blur(sigmaX: 6, sigmaY: 6),
              child: Container(
                key: const ValueKey('pin-badge'),
                width: _badgeSize,
                height: _badgeSize,
                alignment: Alignment.center,
                decoration: BoxDecoration(
                  shape: BoxShape.circle,
                  color: const Color(0xB30F0E0D),
                  border: Border.all(color: VelaColors.accent, width: 1.5),
                ),
                child: Text(
                  '$number',
                  style: VelaText.mono12m.copyWith(color: VelaColors.ink),
                ),
              ),
            ),
          ),
        ),
      ),
    ),
  );
}
