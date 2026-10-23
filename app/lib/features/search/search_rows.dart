import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:vela_api_client/vela_api_client.dart';

import '../../core/theme/tokens.dart';
import '../portfolio/avatar.dart';
import '../portfolio/portfolio_data.dart';
import '../story/story_settings.dart';

/// One artist: tap the row to open the portfolio, tap the pill to follow.
/// [following] null hides the pill (the viewer's own account).
class ArtistRow extends StatelessWidget {
  const ArtistRow({
    super.key,
    required this.profile,
    required this.following,
    required this.onOpen,
    required this.onToggle,
  });

  final Profile profile;
  final bool? following;
  final VoidCallback onOpen;
  final void Function(bool currentlyFollowing) onToggle;

  @override
  Widget build(BuildContext context) {
    final sub = [
      if (profile.discipline?.trim().isNotEmpty ?? false)
        profile.discipline!.trim(),
      if (profile.city?.trim().isNotEmpty ?? false) profile.city!.trim(),
    ].join(' · ');
    final isFollowing = following;
    return Row(
      children: [
        Expanded(
          child: Semantics(
            button: true,
            label: 'Artist ${profile.displayName}',
            excludeSemantics: true,
            onTap: onOpen,
            child: GestureDetector(
              key: ValueKey('search-artist-${profile.id}'),
              behavior: HitTestBehavior.opaque,
              onTap: onOpen,
              child: ConstrainedBox(
                constraints: const BoxConstraints(minHeight: 56),
                child: Row(
                  children: [
                    Avatar(
                      name: profile.displayName,
                      url: profile.avatarUrl,
                      size: 40,
                    ),
                    const SizedBox(width: 12),
                    Expanded(
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        mainAxisSize: MainAxisSize.min,
                        children: [
                          Text(
                            profile.displayName,
                            style: VelaText.sans14m,
                            maxLines: 1,
                            overflow: TextOverflow.ellipsis,
                          ),
                          if (sub.isNotEmpty)
                            Text(
                              sub,
                              style: VelaText.sans12.copyWith(
                                color: VelaColors.muted,
                              ),
                              maxLines: 1,
                              overflow: TextOverflow.ellipsis,
                            ),
                        ],
                      ),
                    ),
                  ],
                ),
              ),
            ),
          ),
        ),
        if (isFollowing != null)
          Semantics(
            button: true,
            label: isFollowing
                ? 'Unfollow ${profile.displayName}'
                : 'Follow ${profile.displayName}',
            excludeSemantics: true,
            onTap: () => onToggle(isFollowing),
            child: GestureDetector(
              key: ValueKey('search-follow-${profile.id}'),
              behavior: HitTestBehavior.opaque,
              onTap: () => onToggle(isFollowing),
              child: ConstrainedBox(
                constraints: const BoxConstraints(minHeight: 44, minWidth: 44),
                child: Center(
                  child: Container(
                    padding: const EdgeInsets.symmetric(
                      horizontal: 12,
                      vertical: 6,
                    ),
                    decoration: BoxDecoration(
                      borderRadius: BorderRadius.circular(99),
                      border: Border.all(color: VelaColors.outline),
                    ),
                    child: Text(
                      isFollowing ? 'Following' : 'Follow',
                      style: VelaText.sans12m.copyWith(
                        color: isFollowing ? VelaColors.muted : VelaColors.ink,
                      ),
                    ),
                  ),
                ),
              ),
            ),
          ),
      ],
    );
  }
}

/// One story: cover, title and author. The whole row opens the story.
class StoryRow extends ConsumerWidget {
  const StoryRow({super.key, required this.work, required this.onOpen});

  final Work work;
  final VoidCallback onOpen;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final url = workImageUrl(work);
    final factory = ref.watch(storyImageProviderFactoryProvider);
    final chapters = work.chapters.length;
    return Semantics(
      button: true,
      label: 'Story ${work.title} by ${work.author.displayName}',
      excludeSemantics: true,
      onTap: onOpen,
      child: GestureDetector(
        key: ValueKey('search-story-${work.id}'),
        behavior: HitTestBehavior.opaque,
        onTap: onOpen,
        child: ConstrainedBox(
          constraints: const BoxConstraints(minHeight: 88),
          child: Padding(
            padding: const EdgeInsets.symmetric(vertical: 6),
            child: Row(
              children: [
                ClipRRect(
                  borderRadius: BorderRadius.circular(10),
                  child: SizedBox(
                    width: 60,
                    height: 76,
                    child: url == null
                        ? const ColoredBox(color: VelaColors.input)
                        : Image(
                            image: factory(url),
                            fit: BoxFit.cover,
                            errorBuilder: (_, _, _) =>
                                const ColoredBox(color: VelaColors.input),
                          ),
                  ),
                ),
                const SizedBox(width: 14),
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    mainAxisSize: MainAxisSize.min,
                    children: [
                      Text(
                        work.title,
                        style: VelaText.serif24,
                        maxLines: 2,
                        overflow: TextOverflow.ellipsis,
                      ),
                      const SizedBox(height: 4),
                      Text(
                        '${work.author.displayName} · '
                        '$chapters ${chapters == 1 ? 'chapter' : 'chapters'}',
                        style: VelaText.sans12.copyWith(
                          color: VelaColors.muted,
                        ),
                        maxLines: 1,
                        overflow: TextOverflow.ellipsis,
                      ),
                    ],
                  ),
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}
