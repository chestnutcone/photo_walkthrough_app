import 'package:flutter/material.dart';

import '../../core/theme/tokens.dart';
import '../story/photo_layout.dart';
import 'create_state.dart';
import 'pin_draft.dart';

/// Height of the photo area (README: photo 390x420).
const double photoAreaHeight = 420;
const double _hit = 44;
const double _badge = 28;

/// The photograph with its pins. The photo is contained (never cropped) and
/// a tap is mapped back to a fraction of the DISPLAYED photo through the same
/// [PhotoFrame] maths the viewer uses, so a pin lands where the author
/// tapped whatever the screen or the photo's shape.
class PinEditor extends StatelessWidget {
  const PinEditor({
    super.key,
    required this.photo,
    required this.pins,
    required this.ghost,
    required this.editingIndex,
    required this.onTapPhoto,
    required this.onTapPin,
  });

  final DraftPhoto photo;
  final List<PinDraft> pins;
  final PinDraft? ghost;
  final int? editingIndex;
  final ValueChanged<Offset> onTapPhoto;
  final ValueChanged<int> onTapPin;

  @override
  Widget build(BuildContext context) => LayoutBuilder(
    builder: (context, c) {
      final viewport = Size(c.maxWidth, photoAreaHeight);
      final frame = computeContainFrame(
        viewport: viewport,
        aspect: photo.aspect,
      );
      Offset at(double x, double y) => Offset(
        frame.rect.left + x * frame.rect.width,
        frame.rect.top + y * frame.rect.height,
      );
      return SizedBox(
        height: photoAreaHeight,
        child: Semantics(
          label: 'Your photograph. Tap anywhere on it to start a chapter.',
          image: true,
          child: GestureDetector(
            key: const ValueKey('photo-area'),
            behavior: HitTestBehavior.opaque,
            onTapUp: (d) {
              final f = frame.toPhotoFraction(d.localPosition);
              if (f != null) onTapPhoto(f);
            },
            child: Stack(
              clipBehavior: Clip.none,
              children: [
                Positioned.fromRect(
                  key: const ValueKey('photo-box'),
                  rect: frame.rect,
                  child: ExcludeSemantics(
                    child: Image.memory(
                      photo.bytes,
                      cacheWidth:
                          (frame.rect.width *
                                  MediaQuery.devicePixelRatioOf(context))
                              .ceil(),
                      fit: BoxFit.fill,
                      gaplessPlayback: true,
                      filterQuality: FilterQuality.medium,
                    ),
                  ),
                ),
                for (var i = 0; i < pins.length; i++)
                  if (i != editingIndex)
                    _centred(
                      at(pins[i].x, pins[i].y),
                      Semantics(
                        button: true,
                        label: 'Chapter ${i + 1}: ${pins[i].title}. Edit.',
                        excludeSemantics: true,
                        child: GestureDetector(
                          key: ValueKey('pin-$i'),
                          behavior: HitTestBehavior.opaque,
                          onTap: () => onTapPin(i),
                          child: _NumberBadge(number: i + 1),
                        ),
                      ),
                    ),
                if (ghost != null)
                  _centred(
                    at(ghost!.x, ghost!.y),
                    IgnorePointer(
                      child: Semantics(
                        label: 'New chapter position',
                        child: const _GhostPin(key: ValueKey('ghost-pin')),
                      ),
                    ),
                  ),
              ],
            ),
          ),
        ),
      );
    },
  );

  Widget _centred(Offset p, Widget child) => Positioned(
    left: p.dx - _hit / 2,
    top: p.dy - _hit / 2,
    width: _hit,
    height: _hit,
    child: child,
  );
}

class _NumberBadge extends StatelessWidget {
  const _NumberBadge({required this.number});
  final int number;

  @override
  Widget build(BuildContext context) => Center(
    child: Container(
      width: _badge,
      height: _badge,
      alignment: Alignment.center,
      decoration: BoxDecoration(
        color: VelaColors.background.withValues(alpha: 0.72),
        shape: BoxShape.circle,
        border: Border.all(color: VelaColors.accent, width: 1.5),
      ),
      child: Text(
        '$number',
        style: VelaText.mono12m.copyWith(color: VelaColors.accent),
      ),
    ),
  );
}

/// Accent filled dot with an 8pt halo (README).
class _GhostPin extends StatelessWidget {
  const _GhostPin({super.key});

  @override
  Widget build(BuildContext context) => Center(
    child: Container(
      width: 14,
      height: 14,
      decoration: BoxDecoration(
        color: VelaColors.accent,
        shape: BoxShape.circle,
        boxShadow: [
          BoxShadow(
            color: VelaColors.accent.withValues(alpha: 0.28),
            spreadRadius: 8,
          ),
        ],
      ),
    ),
  );
}
