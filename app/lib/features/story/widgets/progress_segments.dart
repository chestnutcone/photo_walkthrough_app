import 'package:flutter/material.dart';

import '../../../core/theme/tokens.dart';
import '../motion.dart';

/// One 3pt segment per chapter; segments up to [step] are filled. Tapping a
/// segment jumps to that chapter. Each segment has a taller invisible tap
/// target (6pt padding above and below).
class ProgressSegments extends StatelessWidget {
  const ProgressSegments({
    super.key,
    required this.count,
    required this.step,
    required this.onJump,
  });

  final int count;
  final int step;
  final ValueChanged<int> onJump;

  @override
  Widget build(BuildContext context) => Row(
    children: [
      for (var i = 0; i < count; i++) ...[
        if (i > 0) const SizedBox(width: 4),
        Expanded(
          child: Semantics(
            button: true,
            label: 'Go to chapter ${i + 1} of $count',
            excludeSemantics: true,
            onTap: () => onJump(i + 1),
            child: GestureDetector(
              key: ValueKey('segment-$i'),
              behavior: HitTestBehavior.opaque,
              onTap: () => onJump(i + 1),
              child: Padding(
                padding: const EdgeInsets.symmetric(vertical: 6),
                child: ClipRRect(
                  borderRadius: BorderRadius.circular(2),
                  child: ColoredBox(
                    color: const Color(0x40FFFFFF),
                    child: SizedBox(
                      height: 3,
                      child: AnimatedFractionallySizedBox(
                        duration: motionDuration(
                          context,
                          const Duration(milliseconds: 400),
                        ),
                        alignment: Alignment.centerLeft,
                        widthFactor: step >= i + 1 ? 1 : 0,
                        child: const ColoredBox(color: VelaColors.ink),
                      ),
                    ),
                  ),
                ),
              ),
            ),
          ),
        ),
      ],
    ],
  );
}
