import 'package:flutter/material.dart';

import '../../core/branding.dart';
import '../../core/theme/tokens.dart';
import 'feed_controller.dart';
import 'feed_format.dart';

/// Wordmark, tabs, search and (signed-in) notifications bell, then the
/// subline.
class FeedHeader extends StatelessWidget {
  const FeedHeader({
    super.key,
    required this.tab,
    required this.tabs,
    required this.onTab,
    required this.onSearch,
    required this.showBell,
    required this.unread,
    required this.onBell,
  });

  final FeedTab tab;
  final List<FeedTab> tabs;
  final ValueChanged<FeedTab> onTab;
  final VoidCallback onSearch;
  final bool showBell;
  final int unread;
  final VoidCallback onBell;

  @override
  Widget build(BuildContext context) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Padding(
          padding: const EdgeInsets.fromLTRB(20, 8, 8, 4),
          child: Row(
            children: [
              Semantics(
                header: true,
                child: Text(Branding.appName, style: VelaText.serif40),
              ),
              const Spacer(),
              Flexible(
                child: FittedBox(
                  fit: BoxFit.scaleDown,
                  child: Row(
                    children: [
                      for (final t in tabs)
                        _TabLabel(
                          tab: t,
                          selected: t == tab,
                          onTap: () => onTab(t),
                        ),
                    ],
                  ),
                ),
              ),
              _IconAction(
                key: const ValueKey('search-button'),
                icon: Icons.search,
                label: 'Search',
                onTap: onSearch,
              ),
              if (showBell)
                _IconAction(
                  key: const ValueKey('bell-button'),
                  icon: Icons.notifications_none,
                  label: unread > 0
                      ? 'Notifications, $unread unread'
                      : 'Notifications',
                  onTap: onBell,
                  badge: unread,
                ),
            ],
          ),
        ),
        Padding(
          padding: const EdgeInsets.fromLTRB(20, 0, 20, 18),
          child: Text(
            'PHOTOGRAPHS, WITH THE STORY LEFT IN',
            style: VelaText.mono11,
          ),
        ),
      ],
    );
  }
}

class _TabLabel extends StatelessWidget {
  const _TabLabel({
    required this.tab,
    required this.selected,
    required this.onTap,
  });

  final FeedTab tab;
  final bool selected;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final label = tab == FeedTab.following ? 'Following' : 'Discover';
    return Semantics(
      button: true,
      selected: selected,
      label: label,
      excludeSemantics: true,
      onTap: onTap,
      child: GestureDetector(
        key: ValueKey('tab-${tab.name}'),
        behavior: HitTestBehavior.opaque,
        onTap: onTap,
        child: Container(
          constraints: const BoxConstraints(minHeight: 44, minWidth: 44),
          padding: const EdgeInsets.symmetric(horizontal: 8),
          alignment: Alignment.center,
          child: Container(
            padding: const EdgeInsets.only(bottom: 2),
            decoration: BoxDecoration(
              border: Border(
                bottom: BorderSide(
                  color: selected ? VelaColors.ink : Colors.transparent,
                ),
              ),
            ),
            child: Text(
              label,
              style: VelaText.sans13m.copyWith(
                color: selected ? VelaColors.ink : VelaColors.faint,
              ),
            ),
          ),
        ),
      ),
    );
  }
}

class _IconAction extends StatelessWidget {
  const _IconAction({
    super.key,
    required this.icon,
    required this.label,
    required this.onTap,
    this.badge = 0,
  });

  final IconData icon;
  final String label;
  final VoidCallback onTap;
  final int badge;

  @override
  Widget build(BuildContext context) => Semantics(
    button: true,
    label: label,
    excludeSemantics: true,
    onTap: onTap,
    child: GestureDetector(
      behavior: HitTestBehavior.opaque,
      onTap: onTap,
      child: SizedBox(
        width: 44,
        height: 44,
        child: Stack(
          alignment: Alignment.center,
          children: [
            Icon(icon, color: VelaColors.ink, size: 24),
            if (badge > 0)
              Positioned(
                top: 5,
                right: 2,
                child: Container(
                  key: const ValueKey('unread-badge'),
                  constraints: const BoxConstraints(minWidth: 16),
                  height: 16,
                  padding: const EdgeInsets.symmetric(horizontal: 4),
                  alignment: Alignment.center,
                  decoration: BoxDecoration(
                    color: VelaColors.accent,
                    borderRadius: BorderRadius.circular(8),
                  ),
                  child: Text(
                    unreadLabel(badge),
                    style: VelaText.mono10.copyWith(
                      color: VelaColors.background,
                      letterSpacing: 0,
                      height: 1,
                    ),
                  ),
                ),
              ),
          ],
        ),
      ),
    ),
  );
}
