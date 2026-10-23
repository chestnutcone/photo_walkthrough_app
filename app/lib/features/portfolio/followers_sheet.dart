import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:vela_api_client/vela_api_client.dart';

import '../../core/theme/tokens.dart';
import '../feed/follow_overrides.dart';
import 'avatar.dart';
import 'follow_toggle.dart';
import 'portfolio_data.dart';
import 'portfolio_format.dart';

const double _loadMoreExtent = 240;

/// Opens the followers of [artistId]. Tapping a row goes to that artist.
Future<void> showFollowersSheet(
  BuildContext context, {
  required String artistId,
  required int count,
}) => showModalBottomSheet<void>(
  context: context,
  isScrollControlled: true,
  useSafeArea: true,
  backgroundColor: VelaColors.sheet,
  barrierColor: const Color(0x66000000),
  shape: const RoundedRectangleBorder(
    borderRadius: BorderRadius.vertical(top: Radius.circular(26)),
  ),
  builder: (_) => FollowersSheet(artistId: artistId, count: count),
);

class FollowersSheet extends ConsumerStatefulWidget {
  const FollowersSheet({
    super.key,
    required this.artistId,
    required this.count,
  });

  final String artistId;
  final int count;

  @override
  ConsumerState<FollowersSheet> createState() => _FollowersSheetState();
}

class _FollowersSheetState extends ConsumerState<FollowersSheet> {
  final _scroll = ScrollController();

  @override
  void initState() {
    super.initState();
    _scroll.addListener(_maybeLoadMore);
  }

  @override
  void dispose() {
    _scroll.dispose();
    super.dispose();
  }

  void _maybeLoadMore() {
    if (!_scroll.hasClients) return;
    if (_scroll.position.extentAfter < _loadMoreExtent) {
      ref
          .read(followersControllerProvider(widget.artistId).notifier)
          .loadMore();
    }
  }

  void _openArtist(Profile p) {
    final router = GoRouter.of(context);
    Navigator.of(context).pop();
    router.push('/artist/${p.handle}');
  }

  @override
  Widget build(BuildContext context) {
    final state = ref.watch(followersControllerProvider(widget.artistId));
    final ctl = ref.read(followersControllerProvider(widget.artistId).notifier);
    final overrides = ref.watch(followOverridesProvider);
    final me = ref.watch(meProvider).value;
    final myId = me?.profile?.id;
    // Fill a short list that never scrolls.
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (mounted) _maybeLoadMore();
    });

    final Widget body;
    if (state.isLoading) {
      body = const Center(child: CircularProgressIndicator(strokeWidth: 2));
    } else if (state.error != null) {
      body = Center(
        child: TextButton(
          key: const ValueKey('followers-retry'),
          onPressed: ctl.reload,
          style: TextButton.styleFrom(minimumSize: const Size(44, 44)),
          child: const Text("Couldn't load followers. Retry"),
        ),
      );
    } else if (state.items.isEmpty) {
      body = Center(
        child: Text(
          'No followers yet.',
          style: VelaText.sans14.copyWith(color: VelaColors.muted),
        ),
      );
    } else {
      body = ListView.builder(
        controller: _scroll,
        padding: const EdgeInsets.fromLTRB(20, 0, 20, 24),
        itemCount: state.items.length + 1,
        itemBuilder: (context, i) {
          if (i == state.items.length) {
            if (state.moreError != null) {
              return TextButton(
                key: const ValueKey('followers-more-retry'),
                onPressed: ctl.retryMore,
                child: const Text("Couldn't load more. Retry"),
              );
            }
            return state.loadingMore
                ? const Padding(
                    padding: EdgeInsets.all(16),
                    child: Center(
                      child: SizedBox(
                        width: 20,
                        height: 20,
                        child: CircularProgressIndicator(strokeWidth: 2),
                      ),
                    ),
                  )
                : const SizedBox.shrink();
          }
          final p = state.items[i];
          return _FollowerRow(
            profile: p,
            following: effectiveFollowing(overrides, p.id, p.isFollowing),
            isMe: p.id == myId,
            onOpen: () => _openArtist(p),
            onToggle: (current) => toggleFollowFromProfile(
              context,
              ref,
              id: p.id,
              currentlyFollowing: current,
            ),
          );
        },
      );
    }

    return SizedBox(
      key: const ValueKey('followers-sheet'),
      height: 520,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          const SizedBox(height: 10),
          Center(
            child: Container(
              width: 36,
              height: 4,
              decoration: BoxDecoration(
                color: VelaColors.faint,
                borderRadius: BorderRadius.circular(2),
              ),
            ),
          ),
          Padding(
            padding: const EdgeInsets.fromLTRB(20, 16, 20, 12),
            child: Semantics(
              header: true,
              child: Text(
                followersTitle(widget.count),
                style: VelaText.serif24,
              ),
            ),
          ),
          Expanded(child: body),
        ],
      ),
    );
  }
}

class _FollowerRow extends StatelessWidget {
  const _FollowerRow({
    required this.profile,
    required this.following,
    required this.isMe,
    required this.onOpen,
    required this.onToggle,
  });

  final Profile profile;
  final bool following;
  final bool isMe;
  final VoidCallback onOpen;
  final void Function(bool currentlyFollowing) onToggle;

  @override
  Widget build(BuildContext context) {
    final sub = profile.discipline?.trim() ?? '';
    return Row(
      key: ValueKey('follower-row-${profile.id}'),
      children: [
        Expanded(
          child: Semantics(
            button: true,
            label: 'Artist ${profile.displayName}',
            excludeSemantics: true,
            onTap: onOpen,
            child: GestureDetector(
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
        if (!isMe)
          Semantics(
            button: true,
            label: following
                ? 'Unfollow ${profile.displayName}'
                : 'Follow ${profile.displayName}',
            excludeSemantics: true,
            onTap: () => onToggle(following),
            child: GestureDetector(
              key: ValueKey('follower-follow-${profile.id}'),
              behavior: HitTestBehavior.opaque,
              onTap: () => onToggle(following),
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
                      following ? 'Following' : 'Follow',
                      style: VelaText.sans12m.copyWith(
                        color: following ? VelaColors.muted : VelaColors.ink,
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
