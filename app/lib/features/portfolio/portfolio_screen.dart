import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:vela_api_client/vela_api_client.dart';

import '../../core/api/api_exception.dart';
import '../../core/theme/tokens.dart';
import 'about_tab.dart';
import 'portfolio_data.dart';
import 'portfolio_header.dart';
import 'works_tab.dart';

const double _prefetchExtent = 600;

enum _Tab { works, about }

/// An artist's portfolio: identity, stats, works and about & contact.
/// Missing, blocked and suspended artists all show the same not-found state.
class PortfolioScreen extends ConsumerStatefulWidget {
  const PortfolioScreen({super.key, required this.handle});

  final String handle;

  @override
  ConsumerState<PortfolioScreen> createState() => _PortfolioScreenState();
}

class _PortfolioScreenState extends ConsumerState<PortfolioScreen> {
  final _scroll = ScrollController();
  _Tab _tab = _Tab.works;

  String get _handle => widget.handle.toLowerCase();

  @override
  void initState() {
    super.initState();
    _scroll.addListener(_maybeLoadMore);
  }

  @override
  void didUpdateWidget(PortfolioScreen old) {
    super.didUpdateWidget(old);
    if (old.handle != widget.handle) _tab = _Tab.works;
  }

  @override
  void dispose() {
    _scroll.dispose();
    super.dispose();
  }

  void _maybeLoadMore() {
    if (!mounted || _tab != _Tab.works) return;
    if (_scroll.hasClients && _scroll.position.extentAfter >= _prefetchExtent) {
      return;
    }
    unawaited(ref.read(worksControllerProvider(_handle).notifier).loadMore());
  }

  @override
  Widget build(BuildContext context) {
    final artist = ref.watch(artistProvider(_handle));
    return Scaffold(
      body: artist.when(
        data: _content,
        loading: () => Center(
          child: Semantics(
            label: 'Loading portfolio',
            child: const CircularProgressIndicator(strokeWidth: 2),
          ),
        ),
        error: (e, _) => isArtistUnavailable(e)
            ? const _NotFound()
            : _LoadError(
                offline: e is ApiException && e.code == 'network_error',
                onRetry: () => ref.invalidate(artistProvider(_handle)),
              ),
      ),
    );
  }

  Widget _content(Profile profile) {
    final me = ref.watch(meProvider).value;
    final isOwn = isOwnProfile(me, profile);
    // A short list never scrolls, so ask for more after the frame.
    WidgetsBinding.instance.addPostFrameCallback((_) => _maybeLoadMore());
    return CustomScrollView(
      controller: _scroll,
      slivers: [
        SliverToBoxAdapter(
          child: PortfolioHeader(
            handle: _handle,
            profile: profile,
            isOwn: isOwn,
          ),
        ),
        SliverPersistentHeader(
          pinned: true,
          delegate: _TabsDelegate(
            worksCount: profile.workCount,
            selected: _tab,
            onSelect: (t) => setState(() => _tab = t),
          ),
        ),
        if (_tab == _Tab.works)
          WorksSlivers(handle: _handle, isOwn: isOwn)
        else
          SliverToBoxAdapter(
            child: AboutTab(
              profile: profile,
              isOwn: isOwn,
              onContact: () => openProfileContact(context, ref, profile),
            ),
          ),
      ],
    );
  }
}

class _TabsDelegate extends SliverPersistentHeaderDelegate {
  const _TabsDelegate({
    required this.worksCount,
    required this.selected,
    required this.onSelect,
  });

  final int worksCount;
  final _Tab selected;
  final void Function(_Tab) onSelect;

  @override
  double get minExtent => 48;
  @override
  double get maxExtent => 48;

  @override
  bool shouldRebuild(_TabsDelegate old) =>
      old.worksCount != worksCount || old.selected != selected;

  @override
  Widget build(BuildContext context, double shrinkOffset, bool overlaps) =>
      Container(
        padding: const EdgeInsets.symmetric(horizontal: 20),
        decoration: const BoxDecoration(
          color: VelaColors.background,
          border: Border(bottom: BorderSide(color: VelaColors.hairline)),
        ),
        child: SingleChildScrollView(
          scrollDirection: Axis.horizontal,
          child: Row(
            children: [
              _TabLabel(
                key: const ValueKey('tab-works'),
                label: 'Works · $worksCount',
                active: selected == _Tab.works,
                onTap: () => onSelect(_Tab.works),
              ),
              const SizedBox(width: 24),
              _TabLabel(
                key: const ValueKey('tab-about'),
                label: 'About & contact',
                active: selected == _Tab.about,
                onTap: () => onSelect(_Tab.about),
              ),
            ],
          ),
        ),
      );
}

class _TabLabel extends StatelessWidget {
  const _TabLabel({
    super.key,
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
    label: label,
    excludeSemantics: true,
    onTap: onTap,
    child: GestureDetector(
      behavior: HitTestBehavior.opaque,
      onTap: onTap,
      child: Container(
        constraints: const BoxConstraints(minHeight: 48, minWidth: 44),
        alignment: Alignment.center,
        decoration: BoxDecoration(
          border: Border(
            bottom: BorderSide(
              color: active ? VelaColors.ink : Colors.transparent,
              width: 1.5,
            ),
          ),
        ),
        child: Text(
          label,
          style: VelaText.sans14m.copyWith(
            color: active ? VelaColors.ink : VelaColors.faint,
          ),
        ),
      ),
    ),
  );
}

class _NotFound extends StatelessWidget {
  const _NotFound();

  @override
  Widget build(BuildContext context) => SafeArea(
    child: Center(
      key: const ValueKey('portfolio-not-found'),
      child: Padding(
        padding: const EdgeInsets.all(32),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Text('Artist not found', style: VelaText.serif28),
            const SizedBox(height: 10),
            Text(
              "This portfolio isn't available.",
              style: VelaText.sans14.copyWith(color: VelaColors.muted),
            ),
            const SizedBox(height: 20),
            TextButton(
              onPressed: () =>
                  context.canPop() ? context.pop() : context.go('/'),
              style: TextButton.styleFrom(minimumSize: const Size(44, 44)),
              child: const Text('Back to feed'),
            ),
          ],
        ),
      ),
    ),
  );
}

class _LoadError extends StatelessWidget {
  const _LoadError({required this.offline, required this.onRetry});

  final bool offline;
  final VoidCallback onRetry;

  @override
  Widget build(BuildContext context) => SafeArea(
    child: Center(
      child: Padding(
        padding: const EdgeInsets.all(32),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Text(
              offline ? "You're offline" : "Couldn't load this portfolio",
              style: VelaText.serif28,
              textAlign: TextAlign.center,
            ),
            const SizedBox(height: 10),
            Text(
              offline
                  ? 'Check your connection and try again.'
                  : 'Something went wrong. Try again in a moment.',
              textAlign: TextAlign.center,
              style: VelaText.sans14.copyWith(color: VelaColors.muted),
            ),
            const SizedBox(height: 20),
            TextButton(
              key: const ValueKey('portfolio-retry'),
              onPressed: onRetry,
              style: TextButton.styleFrom(minimumSize: const Size(44, 44)),
              child: const Text('Retry'),
            ),
          ],
        ),
      ),
    ),
  );
}
