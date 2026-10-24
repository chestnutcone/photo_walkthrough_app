import 'dart:ui' show ImageFilter;

import 'package:flutter/material.dart';
import 'package:vela_api_client/vela_api_client.dart';

import '../../../core/theme/tokens.dart';
import '../story_controller.dart';
import 'progress_segments.dart';

const _avatarColors = [
  Color(0xFFD39A74),
  Color(0xFF8FB8B0),
  Color(0xFFD8C08A),
  Color(0xFFC9C3B8),
];

/// Coloured circle with the artist's initial (avatar images are not part of
/// the story chrome).
class ArtistAvatar extends StatelessWidget {
  const ArtistAvatar({
    super.key,
    required this.name,
    required this.seed,
    this.size = 32,
  });

  final String name;
  final String seed;
  final double size;

  @override
  Widget build(BuildContext context) {
    final sum = seed.codeUnits.fold<int>(0, (a, b) => a + b);
    return Container(
      width: size,
      height: size,
      alignment: Alignment.center,
      decoration: BoxDecoration(
        shape: BoxShape.circle,
        color: _avatarColors[sum % _avatarColors.length],
      ),
      child: Text(
        name.isEmpty ? '?' : name.characters.first.toUpperCase(),
        style: VelaText.serif18.copyWith(
          fontSize: size * 17 / 32,
          color: VelaColors.background,
        ),
      ),
    );
  }
}

/// Progress segments (Story mode), artist link, Story | Explore pill, close.
class StoryTopBar extends StatelessWidget {
  const StoryTopBar({
    super.key,
    required this.work,
    required this.mode,
    required this.step,
    required this.onJump,
    required this.onMode,
    required this.onClose,
    required this.onArtist,
  });

  final Work work;
  final StoryMode mode;
  final int step;
  final ValueChanged<int> onJump;
  final ValueChanged<StoryMode> onMode;

  /// Null hides the close button (the web viewer has nowhere to go back to).
  final VoidCallback? onClose;

  /// Null makes the artist name inert.
  final VoidCallback? onArtist;

  @override
  Widget build(BuildContext context) {
    final showSegments = mode == StoryMode.story && work.chapters.isNotEmpty;
    return Column(
      mainAxisSize: MainAxisSize.min,
      children: [
        if (showSegments)
          // The 15pt tap-target box starts at the bar's y; the 3pt bar sits 6pt
          // inside it, so lift the box to put the bar at the handoff's y and
          // leave the row 12pt below the bar.
          Transform.translate(
            offset: const Offset(0, -6),
            child: ProgressSegments(
              count: work.chapters.length,
              step: step,
              onJump: onJump,
            ),
          ),
        Row(
          children: [
            Expanded(
              child: Align(
                alignment: Alignment.centerLeft,
                child: _ArtistButton(work: work, onTap: onArtist),
              ),
            ),
            const SizedBox(width: 10),
            _ModePill(mode: mode, onMode: onMode),
            if (onClose case final close?) ...[
              const SizedBox(width: 10),
              _CloseButton(onTap: close),
            ],
          ],
        ),
      ],
    );
  }
}

class _ArtistButton extends StatelessWidget {
  const _ArtistButton({required this.work, required this.onTap});

  final Work work;
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) => Semantics(
    button: true,
    label: 'Open ${work.author.displayName} portfolio',
    excludeSemantics: true,
    onTap: onTap,
    child: GestureDetector(
      onTap: onTap,
      behavior: HitTestBehavior.opaque,
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          ArtistAvatar(name: work.author.displayName, seed: work.author.handle),
          const SizedBox(width: 9),
          Flexible(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              mainAxisSize: MainAxisSize.min,
              children: [
                Text(
                  work.author.displayName,
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: VelaText.sans13m,
                ),
                Text(
                  work.title,
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: VelaText.sans11.copyWith(
                    color: VelaColors.ink.withValues(alpha: 0.75),
                  ),
                ),
              ],
            ),
          ),
        ],
      ),
    ),
  );
}

class _ModePill extends StatelessWidget {
  const _ModePill({required this.mode, required this.onMode});

  final StoryMode mode;
  final ValueChanged<StoryMode> onMode;

  @override
  Widget build(BuildContext context) => ClipRRect(
    borderRadius: BorderRadius.circular(99),
    child: BackdropFilter(
      filter: ImageFilter.blur(sigmaX: 10, sigmaY: 10),
      child: Container(
        padding: const EdgeInsets.all(3),
        decoration: BoxDecoration(
          color: const Color(0x990F0E0D),
          borderRadius: BorderRadius.circular(99),
          border: Border.all(color: const Color(0x1FF2EEE6)),
        ),
        child: Row(
          mainAxisSize: MainAxisSize.min,
          children: [
            _ModeItem(
              label: 'Story',
              active: mode == StoryMode.story,
              onTap: () => onMode(StoryMode.story),
            ),
            _ModeItem(
              label: 'Explore',
              active: mode == StoryMode.explore,
              onTap: () => onMode(StoryMode.explore),
            ),
          ],
        ),
      ),
    ),
  );
}

class _ModeItem extends StatelessWidget {
  const _ModeItem({
    required this.label,
    required this.active,
    required this.onTap,
  });

  final String label;
  final bool active;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) => Semantics(
    button: true,
    selected: active,
    excludeSemantics: true,
    label: label,
    onTap: onTap,
    child: GestureDetector(
      onTap: onTap,
      behavior: HitTestBehavior.opaque,
      child: Container(
        padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 7),
        decoration: BoxDecoration(
          color: active ? VelaColors.ink : Colors.transparent,
          borderRadius: BorderRadius.circular(99),
        ),
        child: Text(
          label,
          style: VelaText.sans12m.copyWith(
            color: active ? VelaColors.background : VelaColors.ink,
          ),
        ),
      ),
    ),
  );
}

class _CloseButton extends StatelessWidget {
  const _CloseButton({required this.onTap});

  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) => Semantics(
    button: true,
    label: 'Close',
    excludeSemantics: true,
    onTap: onTap,
    child: GestureDetector(
      key: const ValueKey('close'),
      onTap: onTap,
      behavior: HitTestBehavior.opaque,
      child: ClipOval(
        child: BackdropFilter(
          filter: ImageFilter.blur(sigmaX: 10, sigmaY: 10),
          child: Container(
            width: 36,
            height: 36,
            alignment: Alignment.center,
            color: const Color(0x990F0E0D),
            child: Text(
              '×',
              style: VelaText.sans15.copyWith(fontSize: 20, height: 1),
            ),
          ),
        ),
      ),
    ),
  );
}
