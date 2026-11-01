import 'package:flutter/material.dart';

import '../../core/theme/tokens.dart';

/// The bottom bar of the feed (handoff section 6): Feed, a "+" that starts a
/// story, and the viewer's own Portfolio. It is the only way for a signed-in
/// viewer to reach their portfolio, settings and the story editor.
class FeedTabBar extends StatelessWidget {
  const FeedTabBar({
    super.key,
    required this.onFeed,
    required this.onCreate,
    required this.onPortfolio,
  });

  final VoidCallback onFeed;
  final VoidCallback onCreate;
  final VoidCallback onPortfolio;

  @override
  Widget build(BuildContext context) {
    final bottom = MediaQuery.paddingOf(context).bottom;
    return DecoratedBox(
      decoration: const BoxDecoration(
        gradient: LinearGradient(
          begin: Alignment.topCenter,
          end: Alignment.bottomCenter,
          colors: [Color(0x000F0E0D), Color(0xF50F0E0D), Color(0xF50F0E0D)],
          stops: [0, 0.3, 1],
        ),
      ),
      child: Padding(
        padding: EdgeInsets.fromLTRB(20, 14, 20, 8 + bottom),
        child: Row(
          children: [
            Expanded(
              child: _Label(
                key: const ValueKey('tab-bar-feed'),
                text: 'Feed',
                active: true,
                onTap: onFeed,
              ),
            ),
            Semantics(
              button: true,
              label: 'New story',
              excludeSemantics: true,
              onTap: onCreate,
              child: GestureDetector(
                key: const ValueKey('tab-bar-create'),
                behavior: HitTestBehavior.opaque,
                onTap: onCreate,
                child: Container(
                  width: 48,
                  height: 48,
                  decoration: const BoxDecoration(
                    color: VelaColors.ink,
                    shape: BoxShape.circle,
                  ),
                  child: const Icon(
                    Icons.add,
                    color: VelaColors.background,
                    size: 26,
                  ),
                ),
              ),
            ),
            Expanded(
              child: _Label(
                key: const ValueKey('tab-bar-portfolio'),
                text: 'Portfolio',
                active: false,
                onTap: onPortfolio,
              ),
            ),
          ],
        ),
      ),
    );
  }
}

class _Label extends StatelessWidget {
  const _Label({
    super.key,
    required this.text,
    required this.active,
    required this.onTap,
  });

  final String text;
  final bool active;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) => Semantics(
    button: true,
    selected: active,
    label: text,
    excludeSemantics: true,
    onTap: onTap,
    child: GestureDetector(
      behavior: HitTestBehavior.opaque,
      onTap: onTap,
      child: ConstrainedBox(
        constraints: const BoxConstraints(minHeight: 48),
        child: Center(
          child: Text(
            text,
            style: VelaText.sans13s.copyWith(
              color: active ? VelaColors.ink : VelaColors.muted,
            ),
          ),
        ),
      ),
    ),
  );
}
