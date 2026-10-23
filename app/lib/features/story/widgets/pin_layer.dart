import 'dart:ui' show ImageFilter;

import 'package:flutter/material.dart';

import '../../../core/theme/tokens.dart';

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

/// Numbered pins on the camera layer, counter-scaled by 1/[scale] so they keep
/// a constant on-screen size. Centres are pulled inward so the 44pt hit area
/// of an edge pin (x=0, y=1, ...) never leaves the screen.
class PinLayer extends StatelessWidget {
  const PinLayer({
    super.key,
    required this.pins,
    required this.layerSize,
    required this.scale,
    required this.visible,
    required this.onTap,
  });

  final List<PinSpec> pins;
  final Size layerSize;
  final double scale;
  final bool visible;
  final ValueChanged<int> onTap;

  @override
  Widget build(BuildContext context) {
    const half = pinHitSize / 2;
    final inset = half / scale;
    return IgnorePointer(
      ignoring: !visible,
      child: ExcludeSemantics(
        excluding: !visible,
        child: AnimatedOpacity(
          key: const ValueKey('pin-layer'),
          opacity: visible ? 1 : 0,
          duration: const Duration(milliseconds: 400),
          child: Stack(
            children: [
              for (var i = 0; i < pins.length; i++)
                Positioned(
                  left:
                      _centre(
                        pins[i].layerFraction.dx,
                        layerSize.width,
                        inset,
                      ) -
                      half,
                  top:
                      _centre(
                        pins[i].layerFraction.dy,
                        layerSize.height,
                        inset,
                      ) -
                      half,
                  width: pinHitSize,
                  height: pinHitSize,
                  child: Transform.scale(
                    scale: 1 / scale,
                    child: _Pin(
                      key: ValueKey('pin-$i'),
                      number: i + 1,
                      label: pins[i].label,
                      onTap: () => onTap(i),
                    ),
                  ),
                ),
            ],
          ),
        ),
      ),
    );
  }
}

double _centre(double fraction, double extent, double inset) =>
    (fraction * extent).clamp(inset, extent - inset).toDouble();

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
