import 'package:flutter/painting.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

/// Viewer-level zoom multiplier from the handoff ("zoomMultiplier" tweak).
/// Scales every chapter's zoom; 1.0 by default.
final zoomMultiplierProvider = Provider<double>((_) => 1.0);

/// Whether autoplay starts switched on. Off by default; the handoff treats it
/// as an optional viewer setting.
final storyAutoplayDefaultProvider = Provider<bool>((_) => false);

/// Builds the [ImageProvider] for a photo URL. Tests override this so no
/// network is touched.
typedef StoryImageProviderFactory = ImageProvider Function(String url);

final storyImageProviderFactoryProvider = Provider<StoryImageProviderFactory>(
  (_) =>
      (url) => NetworkImage(url),
);
