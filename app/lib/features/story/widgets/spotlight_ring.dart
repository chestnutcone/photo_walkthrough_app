import 'package:flutter/material.dart';

import '../../../core/theme/tokens.dart';

/// Diameter of the spotlight ring.
const double spotlightDiameter = 132;

/// Dim colour outside the ring: rgba(8,7,6,.5).
const Color _dim = Color(0x80080706);

/// 132pt ring with a 1.5px accent border that dims everything else.
/// [center] is in screen coordinates; the caller animates it.
class SpotlightRing extends StatelessWidget {
  const SpotlightRing({super.key, required this.center, required this.visible});

  final Offset center;
  final bool visible;

  @override
  Widget build(BuildContext context) => IgnorePointer(
    child: ExcludeSemantics(
      child: AnimatedOpacity(
        opacity: visible ? 1 : 0,
        duration: const Duration(milliseconds: 500),
        child: SizedBox.expand(
          child: CustomPaint(painter: _RingPainter(center)),
        ),
      ),
    ),
  );
}

class _RingPainter extends CustomPainter {
  const _RingPainter(this.center);

  final Offset center;

  @override
  void paint(Canvas canvas, Size size) {
    const radius = spotlightDiameter / 2;
    final hole = Path()
      ..addRect(Offset.zero & size)
      ..addOval(Rect.fromCircle(center: center, radius: radius))
      ..fillType = PathFillType.evenOdd;
    canvas.drawPath(hole, Paint()..color = _dim);
    canvas.drawCircle(
      center,
      radius - 0.75,
      Paint()
        ..style = PaintingStyle.stroke
        ..strokeWidth = 1.5
        ..color = VelaColors.accent,
    );
  }

  @override
  bool shouldRepaint(_RingPainter old) => old.center != center;
}
