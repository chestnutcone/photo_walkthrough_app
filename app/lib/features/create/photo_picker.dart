import 'dart:typed_data';

import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:image_picker/image_picker.dart';

enum PhotoSource { library, camera }

/// A photograph chosen by the author. Never logged.
class PickedPhoto {
  const PickedPhoto(this.bytes);
  final Uint8List bytes;
}

/// Seam over the platform picker so tests can supply photos.
abstract interface class PhotoPicker {
  /// Null when the author cancels.
  Future<PickedPhoto?> pick(PhotoSource source);
}

/// Longest side asked of the platform picker. Re-encoding at this size also
/// bakes in orientation on Android and iOS and turns HEIC into JPEG, which
/// the server accepts (JPEG, PNG, WebP only).
const int pickerMaxSide = 4096;
const int pickerQuality = 90;

class ImagePickerPhotoPicker implements PhotoPicker {
  ImagePickerPhotoPicker([ImagePicker? picker])
    : _picker = picker ?? ImagePicker();

  final ImagePicker _picker;

  @override
  Future<PickedPhoto?> pick(PhotoSource source) async {
    final file = await _picker.pickImage(
      source: source == PhotoSource.camera
          ? ImageSource.camera
          : ImageSource.gallery,
      maxWidth: pickerMaxSide.toDouble(),
      maxHeight: pickerMaxSide.toDouble(),
      imageQuality: pickerQuality,
      requestFullMetadata: false,
    );
    if (file == null) return null;
    return PickedPhoto(await file.readAsBytes());
  }
}

final photoPickerProvider = Provider<PhotoPicker>(
  (_) => ImagePickerPhotoPicker(),
);
