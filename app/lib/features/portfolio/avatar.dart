import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/theme/tokens.dart';
import '../story/story_settings.dart';

/// Round avatar: the artist's photo when there is one, otherwise the initial
/// on the accent colour. Decorative; the name next to it carries the meaning.
class Avatar extends ConsumerWidget {
  const Avatar({
    super.key,
    required this.name,
    required this.size,
    this.url,
    this.borderWidth = 0,
  });

  final String name;
  final String? url;
  final double size;
  final double borderWidth;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final initial = name.trim().isEmpty
        ? '?'
        : name.trim().characters.first.toUpperCase();
    final fallback = Center(
      child: Text(
        initial,
        style: VelaText.serif18.copyWith(
          color: VelaColors.background,
          fontSize: size * 0.45,
        ),
      ),
    );
    final photo = url;
    return ExcludeSemantics(
      child: Container(
        width: size,
        height: size,
        clipBehavior: Clip.antiAlias,
        decoration: BoxDecoration(
          color: VelaColors.accent,
          shape: BoxShape.circle,
          border: borderWidth > 0
              ? Border.all(color: VelaColors.background, width: borderWidth)
              : null,
        ),
        child: photo == null || photo.isEmpty
            ? fallback
            : Image(
                image: ref.watch(storyImageProviderFactoryProvider)(photo),
                fit: BoxFit.cover,
                errorBuilder: (_, _, _) => fallback,
              ),
      ),
    );
  }
}
