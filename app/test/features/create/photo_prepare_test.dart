import 'dart:typed_data';

import 'package:flutter_test/flutter_test.dart';
import 'package:image/image.dart' as img;
import 'package:vela_app/features/create/photo_info.dart';

import 'support/create_harness.dart';

/// Final review I-3 / M-7: rotated JPEGs are re-encoded in pure Dart, so the
/// client caps the pixels it will decode, runs the work in an isolate that is
/// killed on timeout, and never uploads a rotated photo it could not decode.
void main() {
  /// [jpeg] with its SOF size patched to [w] x [h] (header only: the pixel
  /// data no longer matches, which is fine for size checks).
  Uint8List withSofSize(Uint8List jpeg, int w, int h) {
    final b = Uint8List.fromList(jpeg);
    for (var i = 2; i + 9 < b.length; i++) {
      if (b[i] == 0xFF && (b[i + 1] == 0xC0 || b[i + 1] == 0xC2)) {
        ByteData.sublistView(b)
          ..setUint16(i + 5, h)
          ..setUint16(i + 7, w);
        return b;
      }
    }
    throw StateError('no SOF');
  }

  test(
    'the client pixel cap is below the server limit and fits the picker',
    () {
      expect(maxPhotoPixels, lessThan(60000000));
      expect(maxPhotoPixels, greaterThanOrEqualTo(4096 * 4096));
    },
  );

  test('a rotated JPEG that cannot be decoded is refused, not stripped', () {
    // Headers intact (SOF, EXIF orientation 6) but no Huffman tables.
    final broken = _withoutSegment(jpegWithOrientation(orientation: 6), 0xC4);
    expect(readPhotoMeta(broken)?.orientation, 6);
    expect(
      () => bakePhotoOrientation(broken),
      throwsA(isA<PhotoUnreadableException>()),
    );
  });

  test('an upright JPEG that cannot be decoded is only stripped', () {
    final src = jpegWithOrientation(orientation: 1, gps: true);
    final out = bakePhotoOrientation(src);
    expect(String.fromCharCodes(out).contains('Exif'), isFalse);
  });

  test('a rotated JPEG over the cap is refused before decoding', () {
    final huge = withSofSize(jpegWithOrientation(orientation: 6), 6000, 4100);
    expect(
      readPhotoMeta(huge)!.width * readPhotoMeta(huge)!.height,
      greaterThan(maxPhotoPixels),
    );
    expect(
      () => bakePhotoOrientation(huge),
      throwsA(isA<PhotoTooLargeException>()),
    );
  });

  group('isolate normalizer', () {
    test('a large rotated photo comes back upright without EXIF', () async {
      // 12 MP: the size a phone camera gives after the picker's 4096 cap.
      final src = jpegWithOrientation(w: 4000, h: 3000, gps: true);
      final out = await normalizePhotoInIsolate(
        src,
        timeout: const Duration(minutes: 2),
      );
      final meta = readPhotoMeta(out)!;
      expect((meta.width, meta.height, meta.orientation), (3000, 4000, 1));
      expect(String.fromCharCodes(out).contains('Exif'), isFalse);
      expect(img.decodeJpg(out)!.exif.gpsIfd.hasGPSLatitude, isFalse);
    }, timeout: const Timeout(Duration(minutes: 3)));

    test(
      'it gives up with PhotoPrepareTimeout when the work runs long',
      () async {
        final src = jpegWithOrientation(w: 3000, h: 2000);
        await expectLater(
          normalizePhotoInIsolate(src, timeout: Duration.zero),
          throwsA(isA<PhotoPrepareTimeout>()),
        );
      },
    );

    test(
      'an unreadable rotated photo surfaces as PhotoUnreadableException',
      () async {
        final broken = _withoutSegment(
          jpegWithOrientation(orientation: 8),
          0xC4,
        );
        await expectLater(
          normalizePhotoInIsolate(broken, timeout: const Duration(seconds: 30)),
          throwsA(isA<PhotoUnreadableException>()),
        );
      },
    );

    test('a photo with no metadata passes through unchanged', () async {
      final out = await normalizePhotoInIsolate(
        tinyPng,
        timeout: const Duration(seconds: 30),
      );
      expect(out, tinyPng);
    });
  });
}

/// [b] without every segment with [marker] before the scan.
Uint8List _withoutSegment(Uint8List b, int marker) {
  final out = BytesBuilder()..add(b.sublist(0, 2));
  var i = 2;
  while (i + 4 <= b.length && b[i] == 0xFF && b[i + 1] != 0xDA) {
    final len = b[i + 2] << 8 | b[i + 3];
    if (b[i + 1] != marker) out.add(b.sublist(i, i + 2 + len));
    i += 2 + len;
  }
  out.add(b.sublist(i));
  return out.toBytes();
}
