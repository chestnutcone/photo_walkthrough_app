import 'package:flutter/painting.dart';

/// Where the photo sits inside the camera layer, and how pin fractions map
/// onto the layer (the space [computeCamera] works in).
class PhotoFrame {
  const PhotoFrame({required this.rect, required this.viewport});

  /// Photo rectangle in layer coordinates (may overflow the layer on cover).
  final Rect rect;
  final Size viewport;

  /// Maps a pin's fraction of the photo to a fraction of the layer.
  Offset toLayer(Offset pin) => Offset(
    (rect.left + pin.dx * rect.width) / viewport.width,
    (rect.top + pin.dy * rect.height) / viewport.height,
  );
}

/// Lays the photo out in the viewport.
///
/// The handoff uses `object-fit: cover`. Cover crops, though, and a pin on a
/// cropped-away edge would be unreachable, so cover is used only when every
/// pin stays inside the layer; otherwise the photo is contained (letterboxed).
/// An unknown [aspect] (width / height) fills the viewport.
PhotoFrame computePhotoFrame({
  required Size viewport,
  required double? aspect,
  required Iterable<Offset> pins,
}) {
  if (aspect == null || aspect <= 0) {
    return PhotoFrame(rect: Offset.zero & viewport, viewport: viewport);
  }
  final cover = _fit(viewport, aspect, cover: true);
  final frame = PhotoFrame(rect: cover, viewport: viewport);
  if (pins.every((p) => _inside(frame.toLayer(p)))) return frame;
  return PhotoFrame(
    rect: _fit(viewport, aspect, cover: false),
    viewport: viewport,
  );
}

bool _inside(Offset o) => o.dx >= 0 && o.dx <= 1 && o.dy >= 0 && o.dy <= 1;

Rect _fit(Size viewport, double aspect, {required bool cover}) {
  final viewportAspect = viewport.width / viewport.height;
  final fitWidth = cover ? aspect < viewportAspect : aspect >= viewportAspect;
  final w = fitWidth ? viewport.width : viewport.height * aspect;
  final h = fitWidth ? viewport.width / aspect : viewport.height;
  return Rect.fromLTWH(
    (viewport.width - w) / 2,
    (viewport.height - h) / 2,
    w,
    h,
  );
}
