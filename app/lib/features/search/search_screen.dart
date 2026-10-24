import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:vela_api_client/vela_api_client.dart';

import '../../core/api/api_exception.dart';
import '../../core/api/api_providers.dart';
import '../../core/theme/tokens.dart';
import '../feed/feed_states.dart';
import '../feed/follow_overrides.dart';
import '../portfolio/follow_toggle.dart';
import 'recent_searches.dart';
import 'search_controller.dart';
import 'search_rows.dart';
import 'search_rules.dart';

const double _loadMoreExtent = 240;

/// Search for artists and stories. Guests may search; following asks them to
/// sign in. Search text is never logged.
class SearchScreen extends ConsumerStatefulWidget {
  const SearchScreen({super.key});

  @override
  ConsumerState<SearchScreen> createState() => _SearchScreenState();
}

class _SearchScreenState extends ConsumerState<SearchScreen> {
  final _text = TextEditingController();
  final _scroll = ScrollController();

  @override
  void initState() {
    super.initState();
    _scroll.addListener(_maybeLoadMore);
  }

  @override
  void dispose() {
    _scroll.dispose();
    _text.dispose();
    super.dispose();
  }

  SearchResultsController get _ctl =>
      ref.read(searchControllerProvider.notifier);

  void _maybeLoadMore() {
    if (!_scroll.hasClients) return;
    if (_scroll.position.extentAfter < _loadMoreExtent) _ctl.loadMore();
  }

  void _runRecent(String q) {
    _text.value = TextEditingValue(
      text: q,
      selection: TextSelection.collapsed(offset: q.length),
    );
    _ctl.setInput(q);
    _ctl.submit();
  }

  void _clear() {
    _text.clear();
    _ctl.setInput('');
  }

  void _back() => context.canPop() ? context.pop() : context.go('/');

  void _open(String location) {
    _ctl.rememberQuery();
    context.push(location);
  }

  @override
  Widget build(BuildContext context) {
    final state = ref.watch(searchControllerProvider);
    final hasQuery = state.query != null;
    if (hasQuery) {
      WidgetsBinding.instance.addPostFrameCallback((_) {
        if (mounted) _maybeLoadMore();
      });
    }
    return Scaffold(
      backgroundColor: VelaColors.background,
      body: SafeArea(
        bottom: false,
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            _SearchBar(
              controller: _text,
              showClear: state.input.isNotEmpty,
              onChanged: _ctl.setInput,
              onSubmit: (_) => _ctl.submit(),
              onClear: _clear,
              onBack: _back,
            ),
            if (hasQuery) _Tabs(selected: state.tab, onSelect: _ctl.selectTab),
            Expanded(child: hasQuery ? _results(state) : _emptyState(state)),
          ],
        ),
      ),
    );
  }

  Widget _emptyState(SearchState state) {
    final recents = ref.watch(recentSearchesProvider);
    final suggested = ref.watch(suggestedArtistsProvider).value ?? const [];
    final hint = state.tooLong
        ? 'Keep it under $maxSearchLength characters'
        : cleanQuery(state.input).isNotEmpty
        ? 'Type at least $minSearchLength characters'
        : null;
    return ListView(
      padding: const EdgeInsets.fromLTRB(20, 4, 20, 32),
      children: [
        if (hint != null)
          Padding(
            padding: const EdgeInsets.only(bottom: 12),
            child: Text(
              hint,
              style: VelaText.sans13.copyWith(color: VelaColors.muted),
            ),
          ),
        if (recents.isNotEmpty) ...[
          Row(
            children: [
              Expanded(child: Text('RECENT', style: VelaText.mono11)),
              TextButton(
                key: const ValueKey('recent-clear'),
                onPressed: ref.read(recentSearchesProvider.notifier).clear,
                style: TextButton.styleFrom(minimumSize: const Size(44, 44)),
                child: Text(
                  'Clear',
                  style: VelaText.sans13m.copyWith(color: VelaColors.accent),
                ),
              ),
            ],
          ),
          for (var i = 0; i < recents.length; i++)
            _RecentRow(
              key: ValueKey('recent-$i'),
              text: recents[i],
              onTap: () => _runRecent(recents[i]),
            ),
          const SizedBox(height: 16),
        ],
        if (suggested.isNotEmpty) ...[
          Padding(
            padding: const EdgeInsets.symmetric(vertical: 8),
            child: Text('SUGGESTED ARTISTS', style: VelaText.mono11),
          ),
          for (final p in suggested) _artistRow(p),
        ],
      ],
    );
  }

  Widget _artistRow(Profile p) {
    final overrides = ref.watch(followOverridesProvider);
    final signedIn = ref.watch(sessionProvider.select((s) => s.value != null));
    final following = p.isFollowing == null && signedIn
        ? overrides[p.id] // own account (null) unless the viewer just followed
        : effectiveFollowing(overrides, p.id, p.isFollowing);
    return ArtistRow(
      key: ValueKey('artist-${p.id}'),
      profile: p,
      following: following,
      onOpen: () => _open('/artist/${p.handle}'),
      onToggle: (current) => toggleFollowFromProfile(
        context,
        ref,
        id: p.id,
        currentlyFollowing: current,
      ),
    );
  }

  Widget _results(SearchState state) {
    final slice = state.current;
    if (slice == null || slice.isLoading) {
      return const Center(
        child: CircularProgressIndicator(
          strokeWidth: 2,
          color: VelaColors.accent,
        ),
      );
    }
    final error = slice.error;
    if (error != null) {
      return Center(
        child: SingleChildScrollView(
          child: _SearchError(error: error, onRetry: _ctl.retry),
        ),
      );
    }
    if (slice.items.isEmpty) {
      final label = state.tab == SearchTab.artists ? 'artists' : 'stories';
      return Center(
        child: FeedMessage(
          title: 'No $label found',
          body: 'Try a different spelling or fewer words.',
        ),
      );
    }
    return ListView.builder(
      controller: _scroll,
      padding: const EdgeInsets.fromLTRB(20, 4, 20, 32),
      itemCount: slice.items.length + 1,
      itemBuilder: (context, i) {
        if (i == slice.items.length) {
          return FeedMoreFooter(
            loading: slice.loadingMore,
            error: slice.moreError,
            onRetry: _ctl.retryMore,
          );
        }
        return switch (slice.items[i]) {
          ArtistHit(:final profile) => _artistRow(profile),
          StoryHit(:final work) => StoryRow(
            key: ValueKey('story-${work.id}'),
            work: work,
            onOpen: () => _open('/story/${work.id}'),
          ),
        };
      },
    );
  }
}

class _SearchError extends StatelessWidget {
  const _SearchError({required this.error, required this.onRetry});

  final ApiException error;
  final VoidCallback onRetry;

  @override
  Widget build(BuildContext context) {
    final offline = isOffline(error);
    final limited = error.statusCode == 429;
    return FeedMessage(
      title: offline
          ? "You're offline"
          : limited
          ? 'Too many searches'
          : "Couldn't search",
      body: offline
          ? 'Check your connection and try again.'
          : limited
          ? 'Wait a moment and try again.'
          : 'Something went wrong on our side. Try again in a moment.',
      actionLabel: 'Retry',
      actionKey: const ValueKey('search-retry'),
      onAction: onRetry,
    );
  }
}

class _SearchBar extends StatelessWidget {
  const _SearchBar({
    required this.controller,
    required this.showClear,
    required this.onChanged,
    required this.onSubmit,
    required this.onClear,
    required this.onBack,
  });

  final TextEditingController controller;
  final bool showClear;
  final ValueChanged<String> onChanged;
  final ValueChanged<String> onSubmit;
  final VoidCallback onClear;
  final VoidCallback onBack;

  @override
  Widget build(BuildContext context) => Padding(
    padding: const EdgeInsets.fromLTRB(4, 4, 4, 8),
    child: Row(
      children: [
        _IconTap(
          key: const ValueKey('search-back'),
          icon: Icons.arrow_back,
          label: 'Back',
          onTap: onBack,
        ),
        Expanded(
          child: TextField(
            key: const ValueKey('search-field'),
            controller: controller,
            autofocus: true,
            textInputAction: TextInputAction.search,
            onChanged: onChanged,
            onSubmitted: onSubmit,
            style: VelaText.sans15,
            cursorColor: VelaColors.accent,
            decoration: InputDecoration(
              // The label is the field's accessible name; it reads as the
              // hint while empty and is hidden once there is text.
              labelText: 'Search artists and stories',
              floatingLabelBehavior: FloatingLabelBehavior.never,
              labelStyle: VelaText.sans15.copyWith(color: VelaColors.faint),
              border: InputBorder.none,
              isDense: true,
              contentPadding: const EdgeInsets.symmetric(vertical: 14),
            ),
          ),
        ),
        if (showClear)
          _IconTap(
            key: const ValueKey('search-clear'),
            icon: Icons.close,
            label: 'Clear search',
            onTap: onClear,
          ),
      ],
    ),
  );
}

class _IconTap extends StatelessWidget {
  const _IconTap({
    super.key,
    required this.icon,
    required this.label,
    required this.onTap,
  });

  final IconData icon;
  final String label;
  final VoidCallback onTap;

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
        child: Icon(icon, color: VelaColors.ink, size: 22),
      ),
    ),
  );
}

class _Tabs extends StatelessWidget {
  const _Tabs({required this.selected, required this.onSelect});

  final SearchTab selected;
  final ValueChanged<SearchTab> onSelect;

  @override
  Widget build(BuildContext context) => Padding(
    padding: const EdgeInsets.symmetric(horizontal: 12),
    child: Row(
      children: [
        for (final t in SearchTab.values)
          _TabLabel(tab: t, selected: t == selected, onTap: () => onSelect(t)),
      ],
    ),
  );
}

class _TabLabel extends StatelessWidget {
  const _TabLabel({
    required this.tab,
    required this.selected,
    required this.onTap,
  });

  final SearchTab tab;
  final bool selected;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final label = tab == SearchTab.artists ? 'Artists' : 'Stories';
    return Semantics(
      button: true,
      selected: selected,
      label: label,
      excludeSemantics: true,
      onTap: onTap,
      child: GestureDetector(
        key: ValueKey('search-tab-${tab.name}'),
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

class _RecentRow extends StatelessWidget {
  const _RecentRow({super.key, required this.text, required this.onTap});

  final String text;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) => Semantics(
    button: true,
    label: 'Search again: $text',
    excludeSemantics: true,
    onTap: onTap,
    child: GestureDetector(
      behavior: HitTestBehavior.opaque,
      onTap: onTap,
      child: ConstrainedBox(
        constraints: const BoxConstraints(minHeight: 44),
        child: Row(
          children: [
            const Icon(Icons.history, size: 18, color: VelaColors.faint),
            const SizedBox(width: 12),
            Expanded(
              child: Text(
                text,
                style: VelaText.sans14,
                maxLines: 1,
                overflow: TextOverflow.ellipsis,
              ),
            ),
          ],
        ),
      ),
    ),
  );
}
