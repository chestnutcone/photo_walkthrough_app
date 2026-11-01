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
/// Minimum tap target; the visuals stay 36pt inside it.
const double _minTap = kMinTapTarget;

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
    this.onReport,
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

  /// Null hides the More menu (own stories, the web viewer).
  final VoidCallback? onReport;

  @override
  Widget build(BuildContext context) {
    final hasTarget = onClose != null || onReport != null;
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
        // The 48pt tap targets make the row 12pt taller than the 36pt visuals;
        // lift it 6pt so the visuals stay where the handoff puts them.
        Transform.translate(
          offset: Offset(0, hasTarget ? -(_minTap - 36) / 2 : 0),
          child: LayoutBuilder(
            builder: (context, box) {
              final artist = _ArtistButton(work: work, onTap: onArtist);
              // At large font scales or on narrow screens the controls leave
              // the artist no room (E1: name dropped at 2.0 on 320pt), so the
              // artist gets its own line under them.
              final left = box.maxWidth - 10 - _controlsWidth(context);
              if (left < _minArtistWidth) {
                return Column(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    Align(
                      alignment: Alignment.centerRight,
                      child: _controls(context, wrap: true),
                    ),
                    Align(alignment: Alignment.centerLeft, child: artist),
                  ],
                );
              }
              return Row(
                children: [
                  Expanded(
                    child: Align(
                      alignment: Alignment.centerLeft,
                      child: artist,
                    ),
                  ),
                  const SizedBox(width: 10),
                  _controls(context),
                ],
              );
            },
          ),
        ),
      ],
    );
  }

  /// Width [_controls] lays out at, from the pill labels at the current
  /// text scale.
  double _controlsWidth(BuildContext context) {
    final scaler = _pillScaler(context);
    double item(String label) {
      final painter = TextPainter(
        text: TextSpan(text: label, style: VelaText.sans12m),
        textDirection: TextDirection.ltr,
        textScaler: scaler,
      )..layout();
      final w = painter.width + 24;
      painter.dispose();
      return w < _minTap ? _minTap : w;
    }

    var width = item('Story') + item('Explore') + 6;
    if (onReport != null) width += 10 + _minTap;
    if (onClose != null) width += 10 + _minTap;
    return width;
  }

  /// On the two-line layout the controls may wrap too, so nothing overflows
  /// at the largest font scales.
  Widget _controls(BuildContext context, {bool wrap = false}) {
    final children = [
      MediaQuery(
        data: MediaQuery.of(context).copyWith(textScaler: _pillScaler(context)),
        child: _ModePill(mode: mode, onMode: onMode),
      ),
      if (onReport case final report?) _MoreMenu(onReport: report),
      if (onClose case final close?) _CloseButton(onTap: close),
    ];
    if (wrap) {
      return Wrap(
        alignment: WrapAlignment.end,
        crossAxisAlignment: WrapCrossAlignment.center,
        spacing: 10,
        children: children,
      );
    }
    return Row(
      mainAxisSize: MainAxisSize.min,
      children: [
        for (var i = 0; i < children.length; i++) ...[
          if (i > 0) const SizedBox(width: 10),
          children[i],
        ],
      ],
    );
  }

  /// The two short pill labels stop growing at 1.3x so the controls fit
  /// beside the artist; all other text follows the system scale (ruling S2-6).
  static TextScaler _pillScaler(BuildContext context) =>
      MediaQuery.textScalerOf(context).clamp(maxScaleFactor: 1.3);
}

/// When the controls leave the artist less than this, it moves to its own line.
const double _minArtistWidth = 120;

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
      child: ConstrainedBox(
        constraints: const BoxConstraints(minHeight: _minTap),
        child: Row(
          mainAxisSize: MainAxisSize.min,
          children: [
            ArtistAvatar(
              name: work.author.displayName,
              seed: work.author.handle,
            ),
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
    ),
  );
}

class _ModePill extends StatelessWidget {
  const _ModePill({required this.mode, required this.onMode});

  final StoryMode mode;
  final ValueChanged<StoryMode> onMode;

  /// The pill is drawn 36pt tall (handoff); each item's tap target is the
  /// full 48pt row height around it.
  @override
  Widget build(BuildContext context) => Stack(
    children: [
      Positioned.fill(
        top: (_minTap - 36) / 2,
        bottom: (_minTap - 36) / 2,
        child: ClipRRect(
          borderRadius: BorderRadius.circular(99),
          child: BackdropFilter(
            filter: ImageFilter.blur(sigmaX: 10, sigmaY: 10),
            child: DecoratedBox(
              decoration: BoxDecoration(
                color: const Color(0x990F0E0D),
                borderRadius: BorderRadius.circular(99),
                border: Border.all(color: const Color(0x1FF2EEE6)),
              ),
            ),
          ),
        ),
      ),
      Padding(
        padding: const EdgeInsets.symmetric(horizontal: 3),
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
    ],
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
      child: ConstrainedBox(
        constraints: const BoxConstraints(
          minHeight: _minTap,
          minWidth: _minTap,
        ),
        child: Center(
          widthFactor: 1,
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
      ),
    ),
  );
}

enum _MoreAction { report }

class _MoreMenu extends StatelessWidget {
  const _MoreMenu({required this.onReport});

  final VoidCallback onReport;

  @override
  Widget build(BuildContext context) => PopupMenuButton<_MoreAction>(
    key: const ValueKey('story-menu'),
    tooltip: 'More',
    color: VelaColors.sheet,
    // 48pt target around a 36pt icon, like the close button.
    padding: EdgeInsets.zero,
    style: IconButton.styleFrom(
      tapTargetSize: MaterialTapTargetSize.shrinkWrap,
      minimumSize: const Size(_minTap, _minTap),
      padding: EdgeInsets.zero,
    ),
    icon: const Icon(Icons.more_horiz, color: VelaColors.ink, size: 22),
    onSelected: (_) => onReport(),
    itemBuilder: (_) => const [
      PopupMenuItem<_MoreAction>(
        key: ValueKey('story-menu-report'),
        value: _MoreAction.report,
        child: Text('Report story'),
      ),
    ],
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
      child: SizedBox.square(
        dimension: _minTap,
        child: Center(
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
      ),
    ),
  );
}
