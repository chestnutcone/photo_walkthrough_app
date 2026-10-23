import 'dart:ui' show ImageFilter;

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../story_settings.dart';

/// The photograph, drawn into [rect] of the camera layer. A blurred thumbnail
/// sits underneath while the full-size image loads. [onError] fires once per
/// failed load of the full-size image.
class StoryPhoto extends ConsumerWidget {
  const StoryPhoto({
    super.key,
    required this.rect,
    required this.imageUrl,
    required this.thumbUrl,
    required this.attempt,
    required this.onError,
  });

  final Rect rect;
  final String? imageUrl;
  final String? thumbUrl;

  /// Bumped on retry so a failed image is requested again.
  final int attempt;
  final VoidCallback onError;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final factory = ref.watch(storyImageProviderFactoryProvider);
    final full = imageUrl ?? thumbUrl;
    final thumb = (thumbUrl != null && thumbUrl != full) ? thumbUrl : null;
    if (full == null) return const SizedBox.shrink();
    return Positioned.fromRect(
      rect: rect,
      child: Stack(
        fit: StackFit.expand,
        children: [
          if (thumb != null)
            ImageFiltered(
              imageFilter: ImageFilter.blur(sigmaX: 8, sigmaY: 8),
              child: Image(
                image: factory(thumb),
                fit: BoxFit.fill,
                errorBuilder: (_, _, _) => const SizedBox.shrink(),
              ),
            ),
          Image(
            key: ValueKey('photo-$attempt'),
            image: factory(full),
            fit: BoxFit.fill,
            frameBuilder: (context, child, frame, sync) => sync
                ? child
                : AnimatedOpacity(
                    opacity: frame == null ? 0 : 1,
                    duration: const Duration(milliseconds: 300),
                    child: child,
                  ),
            errorBuilder: (context, error, stack) {
              WidgetsBinding.instance.addPostFrameCallback((_) => onError());
              return const SizedBox.shrink();
            },
          ),
        ],
      ),
    );
  }
}
