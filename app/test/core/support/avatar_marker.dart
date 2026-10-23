import 'dart:typed_data';

/// Appended by [markerNormalizer]: proves bytes went through the photo
/// normalizer (orientation + metadata strip) before they were uploaded.
const int normalizedMarker = 0x4D;

Future<Uint8List> markerNormalizer(Uint8List bytes) async =>
    Uint8List.fromList([...bytes, normalizedMarker]);

bool hasMarker(List<int> bytes, List<int> original) =>
    bytes.length == original.length + 1 && bytes.last == normalizedMarker;
