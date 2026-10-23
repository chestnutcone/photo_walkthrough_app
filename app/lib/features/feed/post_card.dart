import 'dart:ui' show ImageFilter;

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:vela_api_client/vela_api_client.dart';

import '../../core/theme/tokens.dart';
import '../story/photo_layout.dart';
import '../story/story_settings.dart';
import 'feed_format.dart';

/// One feed post: artist row, photo with pin previews, title and actions.
class PostCard extends StatelessWidget {
  const PostCard({
    super.key,
    required this.work,
    required this.isFollowing,
    required this.now,
    required this.onOpenStory,
    required this.onOpenArtist,
    required this.onToggleFollow,
  });

  final Work work;
  final bool isFollowing;
  final DateTime now;
  final VoidCallback onOpenStory;
  final VoidCallback onOpenArtist;
  final VoidCallback onToggleFollow;

  @override
  Widget build(BuildContext context) {
    final date = work.publishedAt ?? work.createdAt;
    return Column(
      key: ValueKey('post-card-${work.id}'),
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Row(
          children: [
            Expanded(
              child: _ArtistRow(
                work: work,
                dateLabel: formatFeedDate(date, now),
                onTap: onOpenArtist,
              ),
            ),
            if (!work.isOwner)
              _FollowPill(
                key: ValueKey('follow-pill-${work.id}'),
                following: isFollowing,
                artist: work.author.displayName,
                onTap: onToggleFollow,
              ),
          ],
        ),
        const SizedBox(height: 12),
        _Photo(work: work, onTap: onOpenStory),
        const SizedBox(height: 12),
        Row(
          crossAxisAlignment: CrossAxisAlignment.end,
          children: [
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    work.title,
                    style: VelaText.serif28,
                    maxLines: 2,
                    overflow: TextOverflow.ellipsis,
                  ),
                  const SizedBox(height: 4),
                  Text(
                    feedMeta(work),
                    style: VelaText.mono11.copyWith(color: VelaColors.muted),
                  ),
                ],
              ),
            ),
            const SizedBox(width: 12),
            _WalkThroughPill(onTap: onOpenStory),
          ],
        ),
      ],
    );
  }
}

class _ArtistRow extends StatelessWidget {
  const _ArtistRow({
    required this.work,
    required this.dateLabel,
    required this.onTap,
  });

  final Work work;
  final String dateLabel;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final name = work.author.displayName;
    final initial = name.isEmpty ? '?' : name.characters.first.toUpperCase();
    return Semantics(
      button: true,
      label: 'Artist $name',
      excludeSemantics: true,
      onTap: onTap,
      child: GestureDetector(
        key: ValueKey('author-${work.id}'),
        behavior: HitTestBehavior.opaque,
        onTap: onTap,
        child: ConstrainedBox(
          constraints: const BoxConstraints(minHeight: 44),
          child: Row(
            children: [
              Container(
                width: 34,
                height: 34,
                alignment: Alignment.center,
                decoration: const BoxDecoration(
                  color: VelaColors.accent,
                  shape: BoxShape.circle,
                ),
                child: Text(
                  initial,
                  style: VelaText.serif18.copyWith(
                    color: VelaColors.background,
                  ),
                ),
              ),
              const SizedBox(width: 10),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      name,
                      style: VelaText.sans14m,
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                    ),
                    Text(dateLabel, style: VelaText.mono11),
                  ],
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

class _FollowPill extends StatelessWidget {
  const _FollowPill({
    super.key,
    required this.following,
    required this.artist,
    required this.onTap,
  });

  final bool following;
  final String artist;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) => Semantics(
    button: true,
    label: following ? 'Unfollow $artist' : 'Follow $artist',
    excludeSemantics: true,
    onTap: onTap,
    child: GestureDetector(
      behavior: HitTestBehavior.opaque,
      onTap: onTap,
      child: ConstrainedBox(
        constraints: const BoxConstraints(minHeight: 44, minWidth: 44),
        child: Center(
          child: Container(
            padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 6),
            decoration: BoxDecoration(
              borderRadius: BorderRadius.circular(99),
              border: Border.all(color: const Color(0x2EF2EEE6)),
            ),
            child: Text(
              following ? 'Following' : 'Follow',
              style: VelaText.sans12m.copyWith(
                color: following ? VelaColors.muted : VelaColors.ink,
              ),
            ),
          ),
        ),
      ),
    ),
  );
}

class _WalkThroughPill extends StatelessWidget {
  const _WalkThroughPill({required this.onTap});

  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) => Semantics(
    button: true,
    excludeSemantics: true,
    label: 'Walk through',
    onTap: onTap,
    child: GestureDetector(
      onTap: onTap,
      child: Container(
        padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 10),
        decoration: BoxDecoration(
          color: VelaColors.ink,
          borderRadius: BorderRadius.circular(99),
        ),
        child: Text(
          'Walk through',
          style: VelaText.sans13s.copyWith(color: VelaColors.background),
        ),
      ),
    ),
  );
}

/// Photo 358 x 448 (scaled to the available width) with pin preview dots.
class _Photo extends ConsumerWidget {
  const _Photo({required this.work, required this.onTap});

  final Work work;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final factory = ref.watch(storyImageProviderFactoryProvider);
    final main = work.feedUrl ?? work.thumbUrl ?? work.imageUrl;
    final thumb = work.thumbUrl != main ? work.thumbUrl : null;
    final w = work.imageWidth, h = work.imageHeight;
    final aspect = (w != null && h != null && w > 0 && h > 0) ? w / h : null;
    final pins = [for (final c in work.chapters) Offset(c.x, c.y)];

    return Semantics(
      image: true,
      button: true,
      label: work.altText ?? 'Photograph: ${work.title}',
      onTap: onTap,
      excludeSemantics: true,
      child: GestureDetector(
        key: ValueKey('photo-${work.id}'),
        onTap: onTap,
        child: AspectRatio(
          aspectRatio: 358 / 448,
          child: ClipRRect(
            borderRadius: BorderRadius.circular(14),
            child: ColoredBox(
              color: VelaColors.input,
              child: LayoutBuilder(
                builder: (context, box) {
                  final size = box.biggest;
                  final frame = computePhotoFrame(
                    viewport: size,
                    aspect: aspect,
                    pins: pins,
                  );
                  return Stack(
                    fit: StackFit.expand,
                    children: [
                      if (main != null)
                        Positioned.fromRect(
                          rect: frame.rect,
                          child: _FeedImage(
                            image: factory(main),
                            placeholder: thumb == null ? null : factory(thumb),
                          ),
                        ),
                      for (var i = 0; i < pins.length; i++)
                        _PinDot(
                          key: ValueKey('pin-dot-${work.id}-$i'),
                          center: Offset(
                            frame.toLayer(pins[i]).dx * size.width,
                            frame.toLayer(pins[i]).dy * size.height,
                          ),
                        ),
                    ],
                  );
                },
              ),
            ),
          ),
        ),
      ),
    );
  }
}

/// Blurred thumbnail underneath, full image fading in on top; a quiet icon
/// when the image fails.
class _FeedImage extends StatelessWidget {
  const _FeedImage({required this.image, required this.placeholder});

  final ImageProvider image;
  final ImageProvider? placeholder;

  @override
  Widget build(BuildContext context) => Stack(
    fit: StackFit.expand,
    children: [
      if (placeholder != null)
        ImageFiltered(
          imageFilter: ImageFilter.blur(sigmaX: 8, sigmaY: 8),
          child: Image(
            image: placeholder!,
            fit: BoxFit.cover,
            errorBuilder: (_, _, _) => const SizedBox.shrink(),
          ),
        ),
      Image(
        image: image,
        fit: BoxFit.cover,
        frameBuilder: (context, child, frame, sync) => sync
            ? child
            : AnimatedOpacity(
                opacity: frame == null ? 0 : 1,
                duration: MediaQuery.disableAnimationsOf(context)
                    ? Duration.zero
                    : const Duration(milliseconds: 300),
                child: child,
              ),
        errorBuilder: (_, _, _) => const Center(
          child: Icon(
            Icons.broken_image_outlined,
            color: VelaColors.faint,
            size: 32,
          ),
        ),
      ),
    ],
  );
}

/// 10pt accent dot with a 5pt ring at 25% alpha, centred on [center].
class _PinDot extends StatelessWidget {
  const _PinDot({super.key, required this.center});

  final Offset center;

  static const double _dot = 10;
  static const double _ring = 5;
  static const double _size = _dot + 2 * _ring;

  @override
  Widget build(BuildContext context) => Positioned(
    left: center.dx - _size / 2,
    top: center.dy - _size / 2,
    width: _size,
    height: _size,
    child: ExcludeSemantics(
      child: IgnorePointer(
        child: DecoratedBox(
          decoration: BoxDecoration(
            shape: BoxShape.circle,
            color: VelaColors.accent.withValues(alpha: 0.25),
          ),
          child: const Padding(
            padding: EdgeInsets.all(_ring),
            child: DecoratedBox(
              decoration: BoxDecoration(
                shape: BoxShape.circle,
                color: VelaColors.accent,
              ),
            ),
          ),
        ),
      ),
    ),
  );
}
