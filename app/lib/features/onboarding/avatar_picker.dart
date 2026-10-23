import 'package:flutter/foundation.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../create/create_controller.dart' show maxUploadBytes;
import '../create/create_messages.dart';
import '../create/photo_info.dart';
import '../create/photo_picker.dart';

/// A chosen profile photo, already cleaned: orientation baked in and all
/// metadata (EXIF, GPS, XMP) removed. Held in memory until it is uploaded.
@immutable
class PickedAvatar {
  const PickedAvatar({required this.bytes, required this.contentType});

  final Uint8List bytes;

  /// `image/jpeg`, `image/png` or `image/webp`: what the API accepts.
  final String contentType;
}

/// Outcome of [avatarPreparerProvider]: a photo, a cancel (both fields
/// null), or a user-facing [error].
@immutable
class AvatarPick {
  const AvatarPick({this.avatar, this.error});
  final PickedAvatar? avatar;
  final String? error;
}

/// The one way a profile photo is chosen, for onboarding and edit-profile:
/// the B9 photo picker, then type/size checks, then the B9 normalizer
/// (`photoNormalizerProvider`). Nothing else may feed an avatar upload.
final avatarPreparerProvider = Provider<Future<AvatarPick> Function()>((ref) {
  return () async {
    final PickedPhoto? picked;
    try {
      picked = await ref.read(photoPickerProvider).pick(PhotoSource.library);
    } on PhotoAccessDenied {
      return const AvatarPick(error: photoAccessDeniedMessage);
    } on Object {
      return const AvatarPick(error: "Couldn't open your photos.");
    }
    if (picked == null) return const AvatarPick();
    final meta = readPhotoMeta(picked.bytes);
    if (meta == null || !supportedTypes.contains(meta.contentType)) {
      return const AvatarPick(error: unsupportedTypeMessage);
    }
    if (meta.width * meta.height > maxPhotoPixels) {
      return const AvatarPick(error: tooManyPixelsMessage);
    }
    final Uint8List bytes;
    try {
      bytes = await ref.read(photoNormalizerProvider)(picked.bytes);
    } on Object {
      return const AvatarPick(error: "Couldn't use that photo. Try another.");
    }
    if (bytes.length > maxUploadBytes) {
      return const AvatarPick(error: tooLargeMessage);
    }
    return AvatarPick(
      avatar: PickedAvatar(bytes: bytes, contentType: meta.contentType),
    );
  };
});
