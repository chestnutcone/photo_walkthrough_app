import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:vela_api_client/vela_api_client.dart';

import '../../core/api/api_providers.dart';
import '../../core/require_sign_in.dart';
import '../../core/theme/tokens.dart';
import '../notifications/unread_count_provider.dart';
import 'feed_controller.dart';
import 'feed_header.dart';
import 'feed_states.dart';
import 'follow_overrides.dart';
import 'post_card.dart';

/// Distance from the end of the list at which the next page is requested.
const double _prefetchExtent = 600;
const double _cardGap = 36;

/// The home feed. Guests see Discover only; signed-in viewers also get
/// Following, which is their default tab.
class FeedScreen extends ConsumerStatefulWidget {
  const FeedScreen({super.key});

  @override
  ConsumerState<FeedScreen> createState() => _FeedScreenState();
}

class _FeedScreenState extends ConsumerState<FeedScreen> {
  final _scroll = ScrollController();
  FeedTab? _chosen;

  @override
  void dispose() {
    _scroll.dispose();
    super.dispose();
  }

  void _selectTab(FeedTab tab) {
    if (_scroll.hasClients) _scroll.jumpTo(0);
    setState(() => _chosen = tab);
  }

  Future<void> _refresh(FeedTab tab) {
    ref.invalidate(unreadCountProvider);
    return ref.read(feedControllerProvider(tab).notifier).refresh();
  }

  FeedTab _effectiveTab(bool signedIn) =>
      signedIn ? (_chosen ?? FeedTab.following) : FeedTab.discover;

  void _maybeLoadMore(FeedTab tab) {
    if (!_scroll.hasClients) return;
    final feed = ref.read(feedControllerProvider(tab));
    if (feed.canLoadMore && _scroll.position.extentAfter < _prefetchExtent) {
      ref.read(feedControllerProvider(tab).notifier).loadMore();
    }
  }

  Future<void> _toggleFollow(ArtistSummary author, bool following) async {
    if (!await requireSignIn(context, ref) || !mounted) return;
    final result = await ref
        .read(followOverridesProvider.notifier)
        .toggle(author.id, currentlyFollowing: following);
    if (result == FollowResult.rolledBack && mounted) {
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text("Couldn't update follow. Try again.")),
      );
    }
  }

  @override
  Widget build(BuildContext context) {
    final session = ref.watch(sessionProvider);
    ref.listen(sessionProvider, (_, next) {
      if (next.value == null) _chosen = null;
    });
    final signedIn = session.value != null;
    final tab = _effectiveTab(signedIn);
    final unread = signedIn ? ref.watch(unreadCountProvider).value ?? 0 : 0;

    final sessionLoading = session.isLoading && !session.hasValue;
    if (!sessionLoading) {
      ref.listen(feedControllerProvider(tab), (prev, next) {
        final error = next.refreshError;
        if (error != null && prev?.refreshError != error) {
          ScaffoldMessenger.of(context).showSnackBar(
            SnackBar(
              content: Text(
                isOffline(error)
                    ? "You're offline. Couldn't refresh."
                    : "Couldn't refresh the feed.",
              ),
            ),
          );
        }
      });
    }

    final feed = sessionLoading
        ? const FeedState.loading()
        : ref.watch(feedControllerProvider(tab));
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (mounted && !sessionLoading) _maybeLoadMore(tab);
    });

    return Scaffold(
      backgroundColor: VelaColors.background,
      body: SafeArea(
        bottom: false,
        child: RefreshIndicator(
          color: VelaColors.accent,
          backgroundColor: VelaColors.sheet,
          onRefresh: () => _refresh(tab),
          child: NotificationListener<ScrollNotification>(
            onNotification: (n) {
              if (n.depth == 0) _maybeLoadMore(tab);
              return false;
            },
            child: CustomScrollView(
              controller: _scroll,
              physics: const AlwaysScrollableScrollPhysics(),
              slivers: [
                SliverToBoxAdapter(
                  child: FeedHeader(
                    tab: tab,
                    tabs: signedIn ? FeedTab.values : const [FeedTab.discover],
                    onTab: _selectTab,
                    onSearch: () => context.push('/search'),
                    showBell: signedIn,
                    unread: unread,
                    onBell: () => context.push('/notifications'),
                  ),
                ),
                ..._body(tab, feed),
              ],
            ),
          ),
        ),
      ),
    );
  }

  List<Widget> _body(FeedTab tab, FeedState feed) {
    // Read lazily: touching the notifier while the session is still loading
    // would start a feed request for a tab that may not be the right one.
    FeedController notifier() => ref.read(feedControllerProvider(tab).notifier);
    if (feed.isLoading) {
      return const [SliverToBoxAdapter(child: FeedSkeleton())];
    }
    if (feed.error != null) {
      return [
        SliverToBoxAdapter(
          child: FeedErrorMessage(
            error: feed.error!,
            onRetry: () => notifier().refresh(),
          ),
        ),
      ];
    }
    if (feed.items.isEmpty) {
      return [SliverToBoxAdapter(child: _empty(tab))];
    }
    final overrides = ref.watch(followOverridesProvider);
    final now = ref.watch(clockProvider)();
    return [
      SliverList.separated(
        itemCount: feed.items.length,
        separatorBuilder: (_, _) => const SizedBox(height: _cardGap),
        itemBuilder: (context, i) {
          final work = feed.items[i];
          final following =
              overrides[work.author.id] ?? work.author.isFollowing ?? false;
          return Padding(
            padding: const EdgeInsets.symmetric(horizontal: 16),
            child: PostCard(
              work: work,
              isFollowing: following,
              now: now,
              onOpenStory: () => context.push('/story/${work.id}'),
              onOpenArtist: () => context.push('/artist/${work.author.handle}'),
              onToggleFollow: () => _toggleFollow(work.author, following),
            ),
          );
        },
      ),
      SliverToBoxAdapter(
        child: FeedMoreFooter(
          loading: feed.loadingMore,
          error: feed.moreError,
          onRetry: () => notifier().retryMore(),
        ),
      ),
      const SliverToBoxAdapter(child: SizedBox(height: 96)),
    ];
  }

  Widget _empty(FeedTab tab) => tab == FeedTab.following
      ? FeedMessage(
          title: 'Your feed is quiet',
          body:
              'Follow artists to see their stories here. '
              'Discover is full of new work.',
          actionLabel: 'Browse Discover',
          actionKey: const ValueKey('hint-discover'),
          onAction: () => _selectTab(FeedTab.discover),
        )
      : const FeedMessage(
          title: 'Nothing here yet',
          body: 'New stories will appear as artists publish them.',
        );
}
