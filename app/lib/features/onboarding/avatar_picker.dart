import 'package:flutter/foundation.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:image_picker/image_picker.dart';

/// A chosen profile photo held in memory until onboarding is complete.
@immutable
class PickedAvatar {
  const PickedAvatar({required this.bytes, required this.contentType});

  final Uint8List bytes;

  /// `image/jpeg`, `image/png` or `image/webp`: what the API accepts.
  final String contentType;
}

abstract interface class AvatarPicker {
  /// Null when the user cancels or the file is not a supported image.
  Future<PickedAvatar?> pick();
}

/// Gallery picker. The platform downsizes to 1024 px, far below the API's
/// 25 MB limit; the server builds the 256 px avatar.
class ImagePickerAvatarPicker implements AvatarPicker {
  const ImagePickerAvatarPicker();

  static const _types = {
    'jpg': 'image/jpeg',
    'jpeg': 'image/jpeg',
    'png': 'image/png',
    'webp': 'image/webp',
  };

  @override
  Future<PickedAvatar?> pick() async {
    final file = await ImagePicker().pickImage(
      source: ImageSource.gallery,
      maxWidth: 1024,
      maxHeight: 1024,
      imageQuality: 90,
    );
    if (file == null) return null;
    final ext = file.name.split('.').last.toLowerCase();
    final type = _supported(file.mimeType) ?? _types[ext];
    if (type == null) return null;
    return PickedAvatar(bytes: await file.readAsBytes(), contentType: type);
  }

  static String? _supported(String? mime) =>
      _types.containsValue(mime) ? mime : null;
}

final avatarPickerProvider = Provider<AvatarPicker>(
  (_) => const ImagePickerAvatarPicker(),
);
