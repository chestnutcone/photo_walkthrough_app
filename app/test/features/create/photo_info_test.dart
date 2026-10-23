import 'dart:typed_data';

import 'package:flutter_test/flutter_test.dart';
import 'package:vela_app/features/create/photo_info.dart';

import 'support/create_harness.dart';

void main() {
  test('reads png size and type', () {
    final m = readPhotoMeta(tinyPng)!;
    expect(
      (m.contentType, m.width, m.height, m.orientation),
      ('image/png', 1, 1, 1),
    );
  });

  test('jpeg orientation 6 swaps the displayed size', () {
    final m = readPhotoMeta(jpegWithOrientation())!;
    expect(m.contentType, 'image/jpeg');
    expect((m.width, m.height), (40, 30));
    expect(m.orientation, 6);
    expect((m.displayedWidth, m.displayedHeight), (30, 40));
  });

  test('jpeg orientation 1 keeps the size', () {
    final m = readPhotoMeta(jpegWithOrientation(orientation: 1))!;
    expect((m.displayedWidth, m.displayedHeight), (40, 30));
  });

  test('rejects unknown and truncated data', () {
    expect(readPhotoMeta(Uint8List.fromList([1, 2, 3])), isNull);
    expect(readPhotoMeta(Uint8List.fromList([0xFF, 0xD8, 0xFF])), isNull);
  });

  test('reads webp lossy, lossless and extended headers', () {
    Uint8List riff(String fourcc, List<int> payload) {
      final b = BytesBuilder()
        ..add('RIFF'.codeUnits)
        ..add([0, 0, 0, 0])
        ..add('WEBP'.codeUnits)
        ..add(fourcc.codeUnits)
        ..add([payload.length, 0, 0, 0])
        ..add(payload);
      return b.toBytes();
    }

    // VP8X: flags(4), width-1 (3 bytes LE), height-1 (3 bytes LE)
    final x = riff('VP8X', [0, 0, 0, 0, 99, 0, 0, 49, 0, 0]);
    expect(readPhotoMeta(x)!.width, 100);
    expect(readPhotoMeta(x)!.height, 50);
    // VP8L: signature 0x2f then 14 bits w-1, 14 bits h-1
    final w = 199, h = 99;
    final bits = (w) | (h << 14);
    final l = riff('VP8L', [
      0x2f,
      bits & 255,
      (bits >> 8) & 255,
      (bits >> 16) & 255,
      (bits >> 24) & 255,
    ]);
    expect((readPhotoMeta(l)!.width, readPhotoMeta(l)!.height), (200, 100));
    // VP8: 3 frame tag, 3 start code, w(14 bits) h(14 bits)
    final k = riff('VP8 ', [0, 0, 0, 0x9d, 0x01, 0x2a, 160, 0, 80, 0]);
    expect((readPhotoMeta(k)!.width, readPhotoMeta(k)!.height), (160, 80));
  });

  test('bakePhotoOrientation leaves an upright photo untouched', () {
    final bytes = jpegWithOrientation(orientation: 1);
    expect(bakePhotoOrientation(bytes), same(bytes));
  });
}
