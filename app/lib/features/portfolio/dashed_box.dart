import 'dart:ui' show PathMetric;

import 'package:flutter/material.dart';

/// A rounded box with a dashed outline (README: empty works state).
class DashedBox extends StatelessWidget {
  const DashedBox({
    super.key,
    required this.child,
    required this.color,
    this.radius = 14,
    this.padding = EdgeInsets.zero,
  });

  final Widget child;
  final Color color;
  final double radius;
  final EdgeInsetsGeometry padding;

  @override
  Widget build(BuildContext context) => CustomPaint(
    painter: _DashedPainter(color: color, radius: radius),
    child: Padding(padding: padding, child: child),
  );
}

class _DashedPainter extends CustomPainter {
  const _DashedPainter({required this.color, required this.radius});

  final Color color;
  final double radius;

  static const double _dash = 6;
  static const double _gap = 4;

  @override
  void paint(Canvas canvas, Size size) {
    final paint = Paint()
      ..color = color
      ..style = PaintingStyle.stroke
      ..strokeWidth = 1;
    final outline = Path()
      ..addRRect(
        RRect.fromRectAndRadius(
          (Offset.zero & size).deflate(0.5),
          Radius.circular(radius),
        ),
      );
    for (final PathMetric m in outline.computeMetrics()) {
      for (var d = 0.0; d < m.length; d += _dash + _gap) {
        canvas.drawPath(m.extractPath(d, d + _dash), paint);
      }
    }
  }

  @override
  bool shouldRepaint(_DashedPainter old) =>
      old.color != color || old.radius != radius;
}
