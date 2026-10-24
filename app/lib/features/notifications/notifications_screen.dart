import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/api/api_providers.dart';
import '../../core/require_sign_in.dart';
import '../../core/theme/tokens.dart';
import '../feed/feed_states.dart';
import '../portfolio/avatar.dart';
import 'notification_item.dart';
import 'notifications_controller.dart';
import 'unread_count_provider.dart';

const double _loadMoreExtent = 240;

/// Replies, follows and new stories for the signed-in viewer. The route is
/// sign-in gated, but a session lost while the screen is open shows the
/// sign-in prompt instead of a stale list.
///
/// Refreshes when opened, when the app resumes and on pull-to-refresh. The
/// server already leaves out notifications whose story or reply is hidden,
/// removed or blocked, so nothing about them is shown here.
class NotificationsScreen extends ConsumerStatefulWidget {
  const NotificationsScreen({super.key});

  @override
  ConsumerState<NotificationsScreen> createState() =>
      _NotificationsScreenState();
}

class _NotificationsScreenState extends ConsumerState<NotificationsScreen>
    with WidgetsBindingObserver {
  final _scroll = ScrollController();

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    _scroll.addListener(_maybeLoadMore);
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    _scroll.dispose();
    super.dispose();
  }

  bool get _signedIn => ref.read(sessionProvider).value != null;

  NotificationsController get _ctl =>
      ref.read(notificationsControllerProvider.notifier);

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state == AppLifecycleState.resumed && mounted && _signedIn) {
      unawaited(_refresh());
    }
  }

  Future<void> _refresh() {
    ref.invalidate(unreadCountProvider);
    return _ctl.refresh();
  }

  void _maybeLoadMore() {
    if (!_scroll.hasClients || !_signedIn) return;
    if (_scroll.position.extentAfter < _loadMoreExtent) _ctl.loadMore();
  }

  void _open(NotificationItem item) {
    unawaited(_ctl.markRead(item));
    final location = item.location;
    if (location != null) {
      context.push(location);
    } else {
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('This notification no longer has a page')),
      );
    }
  }

  void _back() => context.canPop() ? context.pop() : context.go('/');

  @override
  Widget build(BuildContext context) {
    final session = ref.watch(sessionProvider);
    final loadingSession = session.isLoading && !session.hasValue;
    final signedIn = session.value != null;
    return Scaffold(
      backgroundColor: VelaColors.background,
      body: SafeArea(
        bottom: false,
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            _Header(onBack: _back),
            Expanded(
              child: loadingSession
                  ? const _Spinner()
                  : signedIn
                  ? _list()
                  : _SignInPrompt(onSignIn: () => requireSignIn(context, ref)),
            ),
          ],
        ),
      ),
    );
  }

  Widget _list() {
    ref.listen(notificationsControllerProvider, (prev, next) {
      final error = next.refreshError;
      if (error != null && prev?.refreshError != error) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(
            content: Text(
              isOffline(error)
                  ? "You're offline. Couldn't refresh."
                  : "Couldn't refresh notifications.",
            ),
          ),
        );
      }
    });
    final state = ref.watch(notificationsControllerProvider);
    final now = ref.watch(clockProvider)();
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (mounted) _maybeLoadMore();
    });

    final Widget body;
    if (state.isLoading) {
      body = const _Spinner();
    } else if (state.error != null) {
      final offline = isOffline(state.error!);
      body = _Scrollable(
        child: FeedMessage(
          title: offline ? "You're offline" : "Couldn't load notifications",
          body: offline
              ? 'Check your connection and try again.'
              : 'Something went wrong on our side. Try again in a moment.',
          actionLabel: 'Retry',
          actionKey: const ValueKey('notifications-retry'),
          onAction: _ctl.retry,
        ),
      );
    } else if (state.items.isEmpty) {
      body = const _Scrollable(
        child: FeedMessage(
          title: 'No notifications yet',
          body:
              'Replies, new followers and new stories from artists you '
              'follow will show up here.',
        ),
      );
    } else {
      body = ListView.builder(
        controller: _scroll,
        physics: const AlwaysScrollableScrollPhysics(),
        padding: const EdgeInsets.only(bottom: 32),
        itemCount: state.items.length + 1,
        itemBuilder: (context, i) {
          if (i == state.items.length) {
            return FeedMoreFooter(
              loading: state.loadingMore,
              error: state.moreError,
              onRetry: _ctl.retryMore,
            );
          }
          final item = state.items[i];
          return _NotificationTile(
            item: item,
            now: now,
            onTap: () => _open(item),
          );
        },
      );
    }
    return RefreshIndicator(
      color: VelaColors.accent,
      backgroundColor: VelaColors.sheet,
      onRefresh: _refresh,
      child: body,
    );
  }
}

class _Header extends StatelessWidget {
  const _Header({required this.onBack});

  final VoidCallback onBack;

  @override
  Widget build(BuildContext context) => Padding(
    padding: const EdgeInsets.fromLTRB(4, 4, 20, 8),
    child: Row(
      children: [
        Semantics(
          button: true,
          label: 'Back',
          excludeSemantics: true,
          onTap: onBack,
          child: GestureDetector(
            key: const ValueKey('notifications-back'),
            behavior: HitTestBehavior.opaque,
            onTap: onBack,
            child: const SizedBox(
              width: 44,
              height: 44,
              child: Icon(Icons.arrow_back, color: VelaColors.ink, size: 22),
            ),
          ),
        ),
        Expanded(
          child: Semantics(
            header: true,
            child: Text(
              'Notifications',
              style: VelaText.serif32,
              maxLines: 1,
              overflow: TextOverflow.ellipsis,
            ),
          ),
        ),
      ],
    ),
  );
}

class _Spinner extends StatelessWidget {
  const _Spinner();

  @override
  Widget build(BuildContext context) => const Center(
    child: CircularProgressIndicator(strokeWidth: 2, color: VelaColors.accent),
  );
}

/// Centers [child] but still lets the page be pulled to refresh.
class _Scrollable extends StatelessWidget {
  const _Scrollable({required this.child});

  final Widget child;

  @override
  Widget build(BuildContext context) => LayoutBuilder(
    builder: (context, box) => ListView(
      physics: const AlwaysScrollableScrollPhysics(),
      children: [
        ConstrainedBox(
          constraints: BoxConstraints(minHeight: box.maxHeight),
          child: Center(child: child),
        ),
      ],
    ),
  );
}

class _SignInPrompt extends StatelessWidget {
  const _SignInPrompt({required this.onSignIn});

  final VoidCallback onSignIn;

  @override
  Widget build(BuildContext context) => Center(
    child: FeedMessage(
      title: 'Sign in to see notifications',
      body: 'Replies, new followers and new stories show up here.',
      actionLabel: 'Sign in',
      actionKey: const ValueKey('notifications-sign-in'),
      onAction: onSignIn,
    ),
  );
}

class _NotificationTile extends StatelessWidget {
  const _NotificationTile({
    required this.item,
    required this.now,
    required this.onTap,
  });

  final NotificationItem item;
  final DateTime now;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final time = relativeTime(item.createdAt, now);
    return Semantics(
      button: true,
      label: '${item.text}, $time${item.read ? '' : ', unread'}',
      excludeSemantics: true,
      onTap: onTap,
      child: GestureDetector(
        key: ValueKey('notification-${item.id}'),
        behavior: HitTestBehavior.opaque,
        onTap: onTap,
        child: ConstrainedBox(
          constraints: const BoxConstraints(minHeight: 64),
          child: Padding(
            padding: const EdgeInsets.symmetric(horizontal: 20, vertical: 10),
            child: Row(
              children: [
                Avatar(
                  name: item.actor.displayName,
                  url: item.actor.avatarUrl,
                  size: 40,
                ),
                const SizedBox(width: 12),
                Expanded(
                  child: Text(
                    item.text,
                    style: item.read
                        ? VelaText.sans14.copyWith(color: VelaColors.secondary)
                        : VelaText.sans14m,
                    maxLines: 3,
                    overflow: TextOverflow.ellipsis,
                  ),
                ),
                const SizedBox(width: 12),
                Text(time, style: VelaText.mono11),
                SizedBox(
                  width: 18,
                  child: item.read
                      ? null
                      : Align(
                          alignment: Alignment.centerRight,
                          child: Container(
                            key: ValueKey('notification-${item.id}-unread'),
                            width: 8,
                            height: 8,
                            decoration: const BoxDecoration(
                              color: VelaColors.accent,
                              shape: BoxShape.circle,
                            ),
                          ),
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
