import 'package:flutter/foundation.dart';
import 'package:vela_api_client/vela_api_client.dart';

/// Default camera zoom for a chapter set to "Zoom in".
const double defaultZoom = 2.4;

/// Limits from the contract (`WorkValidator`).
const int maxPins = 40;
const int maxStoryTitle = 200;
const int maxStoryAlt = 300;
const int maxPinTitle = 120;
const int maxPinBody = 1000;
const int maxPinMeta = 80;
const int maxPinAlt = 300;

/// One chapter being authored. [zoom] null means "Stay put".
@immutable
class PinDraft {
  const PinDraft({
    required this.x,
    required this.y,
    this.zoom = defaultZoom,
    this.title = '',
    this.body = '',
    this.meta,
    this.altText,
  });

  final double x;
  final double y;
  final double? zoom;
  final String title;
  final String body;
  final String? meta;
  final String? altText;

  PinDraft filled({
    required double? zoom,
    required String title,
    required String body,
    String? meta,
    String? altText,
  }) => PinDraft(
    x: x,
    y: y,
    zoom: zoom,
    title: title.trim(),
    body: body.trim(),
    meta: _blankToNull(meta),
    altText: _blankToNull(altText),
  );

  Chapter toChapter() => Chapter(
    x: x,
    y: y,
    zoom: zoom,
    title: title,
    body: body,
    meta: meta,
    altText: altText,
  );

  @override
  bool operator ==(Object other) =>
      other is PinDraft &&
      other.x == x &&
      other.y == y &&
      other.zoom == zoom &&
      other.title == title &&
      other.body == body &&
      other.meta == meta &&
      other.altText == altText;

  @override
  int get hashCode => Object.hash(x, y, zoom, title, body, meta, altText);
}

String? _blankToNull(String? s) {
  final t = s?.trim();
  return t == null || t.isEmpty ? null : t;
}
