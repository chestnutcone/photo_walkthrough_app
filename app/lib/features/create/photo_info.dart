import 'dart:typed_data';

import 'package:flutter/foundation.dart' show compute;
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:image/image.dart' as img;

/// What the server accepts for an original (see `createDraft`).
const supportedTypes = ['image/jpeg', 'image/png', 'image/webp'];

/// Header facts about a photo, read without decoding pixels.
class PhotoMeta {
  const PhotoMeta({
    required this.contentType,
    required this.width,
    required this.height,
    this.orientation = 1,
  });

  final String contentType;

  /// Stored pixel size.
  final int width;
  final int height;

  /// EXIF orientation 1..8 (JPEG only; 1 otherwise).
  final int orientation;

  bool get swapsAxes => orientation >= 5 && orientation <= 8;

  /// Size after EXIF orientation: what the author sees and what the server
  /// stores.
  int get displayedWidth => swapsAxes ? height : width;
  int get displayedHeight => swapsAxes ? width : height;
}

/// Parses the JPEG, PNG or WebP header. Null for anything else or a
/// truncated file.
PhotoMeta? readPhotoMeta(Uint8List b) {
  if (b.length < 12) return null;
  if (b[0] == 0xFF && b[1] == 0xD8) return _jpeg(b);
  if (b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47) {
    return _png(b);
  }
  if (_ascii(b, 0, 'RIFF') && _ascii(b, 8, 'WEBP')) return _webp(b);
  return null;
}

bool _ascii(Uint8List b, int at, String s) {
  if (b.length < at + s.length) return false;
  for (var i = 0; i < s.length; i++) {
    if (b[at + i] != s.codeUnitAt(i)) return false;
  }
  return true;
}

int _u16(Uint8List b, int at, {bool le = false}) =>
    le ? b[at] | b[at + 1] << 8 : b[at] << 8 | b[at + 1];

PhotoMeta? _png(Uint8List b) {
  if (b.length < 24 || !_ascii(b, 12, 'IHDR')) return null;
  final bd = ByteData.sublistView(b);
  final w = bd.getUint32(16);
  final h = bd.getUint32(20);
  return w == 0 || h == 0
      ? null
      : PhotoMeta(contentType: 'image/png', width: w, height: h);
}

PhotoMeta? _webp(Uint8List b) {
  if (b.length < 25) return null;
  if (_ascii(b, 12, 'VP8X')) {
    if (b.length < 30) return null;
    final w = 1 + (b[24] | b[25] << 8 | b[26] << 16);
    final h = 1 + (b[27] | b[28] << 8 | b[29] << 16);
    return PhotoMeta(contentType: 'image/webp', width: w, height: h);
  }
  if (_ascii(b, 12, 'VP8L')) {
    if (b[20] != 0x2f) return null;
    final bits = b[21] | b[22] << 8 | b[23] << 16 | b[24] << 24;
    return PhotoMeta(
      contentType: 'image/webp',
      width: 1 + (bits & 0x3FFF),
      height: 1 + ((bits >> 14) & 0x3FFF),
    );
  }
  if (_ascii(b, 12, 'VP8 ')) {
    if (b.length < 30) return null;
    if (b[23] != 0x9d || b[24] != 0x01 || b[25] != 0x2a) return null;
    return PhotoMeta(
      contentType: 'image/webp',
      width: _u16(b, 26, le: true) & 0x3FFF,
      height: _u16(b, 28, le: true) & 0x3FFF,
    );
  }
  return null;
}

PhotoMeta? _jpeg(Uint8List b) {
  var orientation = 1;
  var i = 2;
  while (i + 4 <= b.length) {
    if (b[i] != 0xFF) return null;
    final marker = b[i + 1];
    if (marker == 0xFF) {
      i++;
      continue;
    }
    if (marker == 0xD8 || (marker >= 0xD0 && marker <= 0xD7) || marker == 1) {
      i += 2;
      continue;
    }
    final len = _u16(b, i + 2);
    if (len < 2 || i + 2 + len > b.length) return null;
    final isSof =
        marker >= 0xC0 &&
        marker <= 0xCF &&
        marker != 0xC4 &&
        marker != 0xC8 &&
        marker != 0xCC;
    if (isSof) {
      if (len < 7) return null;
      final h = _u16(b, i + 5);
      final w = _u16(b, i + 7);
      return w == 0 || h == 0
          ? null
          : PhotoMeta(
              contentType: 'image/jpeg',
              width: w,
              height: h,
              orientation: orientation,
            );
    }
    if (marker == 0xE1) orientation = _exifOrientation(b, i + 4, i + 2 + len);
    i += 2 + len;
  }
  return null;
}

/// Reads tag 0x0112 from the TIFF block of an APP1 segment, or 1.
int _exifOrientation(Uint8List b, int start, int end) {
  if (!_ascii(b, start, 'Exif') || start + 14 > end) return 1;
  final t = start + 6;
  final le = b[t] == 0x49;
  int u16(int at) => _u16(b, at, le: le);
  int u32(int at) => le
      ? b[at] | b[at + 1] << 8 | b[at + 2] << 16 | b[at + 3] << 24
      : b[at] << 24 | b[at + 1] << 16 | b[at + 2] << 8 | b[at + 3];
  final ifd = t + u32(t + 4);
  if (ifd + 2 > end) return 1;
  final count = u16(ifd);
  for (var n = 0; n < count; n++) {
    final e = ifd + 2 + n * 12;
    if (e + 12 > end) return 1;
    if (u16(e) == 0x0112) {
      final v = u16(e + 8);
      return v >= 1 && v <= 8 ? v : 1;
    }
  }
  return 1;
}

/// Returns pixels in the orientation the author saw, with the EXIF tag
/// consumed, so the displayed image, the pin fractions and the stored image
/// all agree. Photos that are already upright are returned unchanged.
Uint8List bakePhotoOrientation(Uint8List bytes) {
  final meta = readPhotoMeta(bytes);
  if (meta == null || meta.contentType != 'image/jpeg') return bytes;
  if (meta.orientation == 1) return bytes;
  final decoded = img.decodeJpg(bytes);
  if (decoded == null) return bytes;
  final upright = img.bakeOrientation(decoded);
  return Uint8List.fromList(img.encodeJpg(upright, quality: 92));
}

/// Runs [bakePhotoOrientation], off the UI isolate by default. Tests
/// override it with a synchronous version.
typedef PhotoNormalizer = Future<Uint8List> Function(Uint8List bytes);

final photoNormalizerProvider = Provider<PhotoNormalizer>(
  (_) =>
      (bytes) => compute(bakePhotoOrientation, bytes),
);
