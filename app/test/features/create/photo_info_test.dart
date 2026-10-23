import 'dart:typed_data';

import 'package:flutter_test/flutter_test.dart';
import 'package:image/image.dart' as img;
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

  group('metadata stripping', () {
    bool hasGps(Uint8List b) => img.decodeJpg(b)!.exif.gpsIfd.hasGPSLatitude;
    bool hasExifSegment(Uint8List b) =>
        String.fromCharCodes(b).contains('Exif');

    test('rotated_photo_loses_gps_and_all_exif', () {
      final src = jpegWithOrientation(gps: true);
      expect(hasGps(src), isTrue, reason: 'fixture must carry GPS');
      final out = bakePhotoOrientation(src);
      expect(hasGps(out), isFalse);
      expect(hasExifSegment(out), isFalse);
    });

    test('upright_photo_loses_gps_without_touching_pixels', () {
      final src = jpegWithOrientation(orientation: 1, gps: true);
      expect(hasGps(src), isTrue);
      final out = bakePhotoOrientation(src);
      expect(hasGps(out), isFalse);
      expect(hasExifSegment(out), isFalse);
      final a = img.decodeJpg(src)!;
      final b = img.decodeJpg(out)!;
      expect(b.getBytes(), a.getBytes(), reason: 'lossless: no re-encode');
    });

    test('photo_without_metadata_is_returned_as_is', () {
      expect(bakePhotoOrientation(tinyPng), same(tinyPng));
    });

    test('png_exif_chunk_is_removed', () {
      final withExif = BytesBuilder()
        ..add(tinyPng.sublist(0, 33)) // signature + IHDR
        ..add([0, 0, 0, 4, 0x65, 0x58, 0x49, 0x66, 1, 2, 3, 4, 0, 0, 0, 0])
        ..add(tinyPng.sublist(33));
      final out = bakePhotoOrientation(withExif.toBytes());
      expect(String.fromCharCodes(out).contains('eXIf'), isFalse);
      expect(out, tinyPng);
    });

    test('webp_exif_chunk_is_removed_and_flag_cleared', () {
      Uint8List chunk(String id, List<int> data) => Uint8List.fromList([
        ...id.codeUnits,
        data.length,
        0,
        0,
        0,
        ...data,
        if (data.length.isOdd) 0,
      ]);
      final vp8x = chunk('VP8X', [0x08, 0, 0, 0, 9, 0, 0, 9, 0, 0]);
      final vp8l = chunk('VP8L', [0x2f, 9, 0, 0x80, 0]);
      final exif = chunk('EXIF', [1, 2, 3, 4]);
      final body = [...'WEBP'.codeUnits, ...vp8x, ...vp8l, ...exif];
      final riff = Uint8List.fromList([
        ...'RIFF'.codeUnits,
        body.length,
        0,
        0,
        0,
        ...body,
      ]);
      final out = bakePhotoOrientation(riff);
      expect(String.fromCharCodes(out).contains('EXIF'), isFalse);
      expect(out[20] & 0x08, 0, reason: 'EXIF flag cleared');
      final size = ByteData.sublistView(out).getUint32(4, Endian.little);
      expect(size, out.length - 8);
      expect(readPhotoMeta(out)!.width, 10);
    });
  });

  group('all orientations', () {
    // Stored 40x30 with the marker centred at (5, 3).
    const w = 40.0, h = 30.0, x = 5.0, y = 3.0;
    final expected = <int, (double, double, int, int)>{
      1: (x, y, 40, 30),
      2: (w - x, y, 40, 30),
      3: (w - x, h - y, 40, 30),
      4: (x, h - y, 40, 30),
      5: (y, x, 30, 40),
      6: (h - y, x, 30, 40),
      7: (h - y, w - x, 30, 40),
      8: (y, w - x, 30, 40),
    };
    for (final e in expected.entries) {
      test('orientation_${e.key}_puts_the_marker_where_the_author_saw_it', () {
        final out = img.decodeJpg(
          bakePhotoOrientation(jpegWithOrientation(orientation: e.key)),
        )!;
        expect((out.width, out.height), (e.value.$3, e.value.$4));
        var sx = 0.0, sy = 0.0, n = 0;
        for (final p in out) {
          if (p.r > 180 && p.g < 90) {
            sx += p.x + 0.5;
            sy += p.y + 0.5;
            n++;
          }
        }
        expect(n, greaterThan(0));
        expect(sx / n, closeTo(e.value.$1, 1.2));
        expect(sy / n, closeTo(e.value.$2, 1.2));
      });
    }
  });
}
