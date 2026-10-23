import 'dart:async';
import 'dart:ui' show ImageFilter;

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:vela_api_client/vela_api_client.dart';

import '../../core/require_sign_in.dart';
import '../../core/theme/tokens.dart';
import '../feed/follow_overrides.dart';
import '../report/report_sheet_hook.dart';
import '../story/story_settings.dart';
import 'avatar.dart';
import 'contact_sheet.dart';
import 'follow_toggle.dart';
import 'followers_sheet.dart';
import 'portfolio_data.dart';
import 'portfolio_format.dart';

const double _coverHeight = 236;

/// Cover, identity, stats, "followed by" line and the action buttons.
class PortfolioHeader extends ConsumerWidget {
  const PortfolioHeader({
    super.key,
    required this.handle,
    required this.profile,
    required this.isOwn,
  });

  final String handle;
  final Profile profile;
  final bool isOwn;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final overrides = ref.watch(followOverridesProvider);
    final followers = adjustedFollowerCount(
      profile.followerCount,
      profile.isFollowing,
      overrides[profile.id],
    );
    final viewer = ref.watch(viewerStatusProvider);
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        _Cover(
          handle: handle,
          profile: profile,
          showMenu: !isOwn && viewer == ViewerStatus.ready,
        ),
        Transform.translate(
          offset: const Offset(0, -46),
          child: Padding(
            padding: const EdgeInsets.symmetric(horizontal: 20),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                _Identity(profile: profile),
                const SizedBox(height: 22),
                _Stats(
                  profile: profile,
                  followers: followers,
                  onFollowers: () => _openFollowers(context, ref, followers),
                ),
                _FollowedBy(
                  profile: profile,
                  total: followers,
                  onTap: () => _openFollowers(context, ref, followers),
                ),
                const SizedBox(height: 16),
                _actionRow(
                  context,
                  ref,
                  viewer,
                  effectiveFollowing(
                    overrides,
                    profile.id,
                    profile.isFollowing,
                  ),
                ),
              ],
            ),
          ),
        ),
      ],
    );
  }

  Widget _actionRow(
    BuildContext context,
    WidgetRef ref,
    ViewerStatus viewer,
    bool following,
  ) {
    if (isOwn) {
      return _OwnActions(
        onEdit: () => context.push('/profile/edit'),
        onSettings: () => context.push('/settings'),
        onNewStory: () => _newStory(context, ref),
      );
    }
    switch (viewer) {
      case ViewerStatus.loading:
        // Own profile or not is not known yet; offer nothing meanwhile.
        return const SizedBox(
          key: ValueKey('portfolio-actions-loading'),
          height: 48,
        );
      case ViewerStatus.failed:
        return TextButton(
          key: const ValueKey('portfolio-me-retry'),
          onPressed: () => ref.invalidate(meProvider),
          style: TextButton.styleFrom(minimumSize: const Size(44, 48)),
          child: const Text("Couldn't load your account. Retry"),
        );
      case ViewerStatus.ready:
        return _Actions(
          following: following,
          onFollow: () => toggleFollowFromProfile(
            context,
            ref,
            id: profile.id,
            currentlyFollowing: following,
          ),
          onContact: () => openProfileContact(context, ref, profile),
        );
    }
  }

  Future<void> _openFollowers(
    BuildContext context,
    WidgetRef ref,
    int count,
  ) async {
    // The followers list needs an account.
    if (!await requireSignIn(context, ref) || !context.mounted) return;
    await showFollowersSheet(context, artistId: profile.id, count: count);
  }

  Future<void> _newStory(BuildContext context, WidgetRef ref) async {
    if (!await requireSignIn(context, ref) || !context.mounted) return;
    unawaited(context.push('/create'));
  }
}

/// Asks guests to sign in, then opens the contact sheet for [profile].
Future<void> openProfileContact(
  BuildContext context,
  WidgetRef ref,
  Profile profile, {
  String? workId,
}) async {
  if (!await requireSignIn(context, ref) || !context.mounted) return;
  await showContactSheet(
    context,
    InquiryTarget(
      artistId: profile.id,
      artistName: profile.displayName,
      replyTime: profile.replyTime,
      workId: workId,
    ),
  );
}

class _Identity extends StatelessWidget {
  const _Identity({required this.profile});
  final Profile profile;

  @override
  Widget build(BuildContext context) => Column(
    crossAxisAlignment: CrossAxisAlignment.start,
    children: [
      Avatar(
        name: profile.displayName,
        url: profile.avatarUrl,
        size: 88,
        borderWidth: 4,
      ),
      const SizedBox(height: 14),
      if (profile.availability?.trim().isNotEmpty ?? false) ...[
        _AvailabilityBadge(text: profile.availability!.trim()),
        const SizedBox(height: 14),
      ],
      Semantics(
        header: true,
        child: Text(profile.displayName, style: VelaText.serif46),
      ),
      if (identityLine(profile).isNotEmpty) ...[
        const SizedBox(height: 14),
        Text(identityLine(profile), style: VelaText.mono11),
      ],
      if (profile.bio.trim().isNotEmpty) ...[
        const SizedBox(height: 14),
        Text(
          profile.bio.trim(),
          style: VelaText.sans15.copyWith(color: VelaColors.secondary),
        ),
      ],
    ],
  );
}

class _Cover extends ConsumerWidget {
  const _Cover({
    required this.handle,
    required this.profile,
    required this.showMenu,
  });

  final String handle;
  final Profile profile;
  final bool showMenu;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final first = ref.watch(
      worksControllerProvider(handle)
          .select((s) => s.items.isEmpty ? null : s.items.first),
    );
    final url = first == null ? null : workImageUrl(first);
    return SizedBox(
      height: _coverHeight,
      child: Stack(
        fit: StackFit.expand,
        children: [
          const ColoredBox(color: VelaColors.input),
          if (url != null)
            ExcludeSemantics(
              child: Image(
                image: ref.watch(storyImageProviderFactoryProvider)(url),
                fit: BoxFit.cover,
                errorBuilder: (_, _, _) => const SizedBox.shrink(),
              ),
            ),
          const DecoratedBox(
            decoration: BoxDecoration(
              gradient: LinearGradient(
                begin: Alignment.topCenter,
                end: Alignment.bottomCenter,
                stops: [0, 0.45, 1],
                colors: [
                  Color(0x730F0E0D),
                  Color(0x000F0E0D),
                  VelaColors.background,
                ],
              ),
            ),
          ),
          SafeArea(
            bottom: false,
            child: Padding(
              padding: const EdgeInsets.fromLTRB(12, 8, 12, 0),
              child: Row(
                children: [
                  _Pill(
                    key: const ValueKey('portfolio-back'),
                    label: '← Feed',
                    semantic: 'Back',
                    onTap: () =>
                        context.canPop() ? context.pop() : context.go('/'),
                  ),
                  const Spacer(),
                  if (showMenu) _Menu(profile: profile),
                ],
              ),
            ),
          ),
          if (first != null)
            Positioned(
              left: 20,
              bottom: 52,
              right: 20,
              child: ExcludeSemantics(
                child: Text(
                  'COVER · ${first.title.toUpperCase()}',
                  style: VelaText.mono10,
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                ),
              ),
            ),
        ],
      ),
    );
  }
}

class _Pill extends StatelessWidget {
  const _Pill({
    super.key,
    required this.label,
    required this.semantic,
    required this.onTap,
  });

  final String label;
  final String semantic;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) => Semantics(
    button: true,
    label: semantic,
    excludeSemantics: true,
    onTap: onTap,
    child: GestureDetector(
      behavior: HitTestBehavior.opaque,
      onTap: onTap,
      child: ConstrainedBox(
        constraints: const BoxConstraints(minHeight: 44, minWidth: 44),
        child: Center(
          child: ClipRRect(
            borderRadius: BorderRadius.circular(99),
            child: BackdropFilter(
              filter: ImageFilter.blur(sigmaX: 8, sigmaY: 8),
              child: Container(
                padding: const EdgeInsets.symmetric(
                  horizontal: 12,
                  vertical: 7,
                ),
                color: const Color(0x990F0E0D),
                child: Text(label, style: VelaText.sans12m),
              ),
            ),
          ),
        ),
      ),
    ),
  );
}

enum _MenuAction { report }

class _Menu extends ConsumerWidget {
  const _Menu({required this.profile});
  final Profile profile;

  @override
  Widget build(BuildContext context, WidgetRef ref) =>
      PopupMenuButton<_MenuAction>(
        key: const ValueKey('portfolio-menu'),
        tooltip: 'More',
        color: VelaColors.sheet,
        constraints: const BoxConstraints(minWidth: 44, minHeight: 44),
        icon: const Icon(Icons.more_horiz, color: VelaColors.ink),
        onSelected: (_) async {
          if (!await requireSignIn(context, ref) || !context.mounted) return;
          await ref.read(reportSheetHook)(
            context,
            targetType: 'user',
            targetId: profile.id,
          );
        },
        itemBuilder: (_) => const [
          PopupMenuItem(
            value: _MenuAction.report,
            child: Text('Report artist'),
          ),
        ],
      );
}

class _AvailabilityBadge extends StatelessWidget {
  const _AvailabilityBadge({required this.text});
  final String text;

  @override
  Widget build(BuildContext context) => Container(
    padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 5),
    decoration: BoxDecoration(
      color: VelaColors.accent.withValues(alpha: 0.14),
      borderRadius: BorderRadius.circular(99),
    ),
    child: Row(
      mainAxisSize: MainAxisSize.min,
      children: [
        Container(
          width: 7,
          height: 7,
          decoration: const BoxDecoration(
            color: VelaColors.accent,
            shape: BoxShape.circle,
          ),
        ),
        const SizedBox(width: 7),
        Flexible(
          child: Text(
            text,
            style: VelaText.sans12m.copyWith(color: VelaColors.accent),
          ),
        ),
      ],
    ),
  );
}

class _Stats extends StatelessWidget {
  const _Stats({
    required this.profile,
    required this.followers,
    required this.onFollowers,
  });

  final Profile profile;
  final int followers;
  final VoidCallback onFollowers;

  @override
  Widget build(BuildContext context) => Container(
    decoration: const BoxDecoration(
      border: Border.symmetric(
        horizontal: BorderSide(color: VelaColors.hairline),
      ),
    ),
    child: Row(
      children: [
        _Stat(value: '${profile.workCount}', label: 'Stories'),
        _Stat(value: '${profile.chapterCount ?? 0}', label: 'Chapters'),
        _Stat(
          key: const ValueKey('stat-followers'),
          value: '$followers',
          label: 'Followers ›',
          semantic: '${followersTitle(followers)}, open list',
          onTap: onFollowers,
        ),
        _Stat(value: '${profile.followingCount}', label: 'Following'),
      ],
    ),
  );
}

class _Stat extends StatelessWidget {
  const _Stat({
    super.key,
    required this.value,
    required this.label,
    this.semantic,
    this.onTap,
  });

  final String value;
  final String label;
  final String? semantic;
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) {
    final child = ConstrainedBox(
      constraints: const BoxConstraints(minHeight: 64),
      child: Padding(
        padding: const EdgeInsets.symmetric(vertical: 12),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Text(value, style: VelaText.serif24),
            const SizedBox(height: 2),
            Text(
              label,
              style: VelaText.sans11.copyWith(color: VelaColors.faint),
            ),
          ],
        ),
      ),
    );
    return Expanded(
      child: Semantics(
        button: onTap != null,
        label: semantic ?? '$value $label',
        excludeSemantics: true,
        onTap: onTap,
        child: onTap == null
            ? child
            : GestureDetector(
                behavior: HitTestBehavior.opaque,
                onTap: onTap,
                child: child,
              ),
      ),
    );
  }
}

/// "Followed by Teo, Mara and 2.4k others" with overlapping avatars. Only
/// signed-in viewers can read the follower list, so guests see no line.
class _FollowedBy extends ConsumerWidget {
  const _FollowedBy({
    required this.profile,
    required this.total,
    required this.onTap,
  });

  final Profile profile;
  final int total;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final signedIn = ref.watch(meProvider).value != null;
    if (!signedIn) return const SizedBox.shrink();
    final list = ref.watch(
      followersControllerProvider(profile.id).select((s) => s.items),
    );
    if (list.isEmpty) return const SizedBox.shrink();
    final shown = list.take(3).toList();
    final line = followedByLine([for (final p in list) p.displayName], total);
    return Semantics(
      button: true,
      label: line,
      excludeSemantics: true,
      onTap: onTap,
      child: GestureDetector(
        key: const ValueKey('followed-by-row'),
        behavior: HitTestBehavior.opaque,
        onTap: onTap,
        child: ConstrainedBox(
          constraints: const BoxConstraints(minHeight: 44),
          child: Row(
            children: [
              SizedBox(
                width: 24.0 + (shown.length - 1) * 16,
                height: 24,
                child: Stack(
                  children: [
                    for (var i = 0; i < shown.length; i++)
                      Positioned(
                        left: i * 16.0,
                        child: Avatar(
                          name: shown[i].displayName,
                          url: shown[i].avatarUrl,
                          size: 24,
                          borderWidth: 2,
                        ),
                      ),
                  ],
                ),
              ),
              const SizedBox(width: 10),
              Expanded(
                child: Text(
                  line,
                  style: VelaText.sans13.copyWith(color: VelaColors.muted),
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

class _Actions extends StatelessWidget {
  const _Actions({
    required this.following,
    required this.onFollow,
    required this.onContact,
  });

  final bool following;
  final VoidCallback onFollow;
  final VoidCallback onContact;

  @override
  Widget build(BuildContext context) => Row(
    children: [
      Expanded(
        child: _ActionButton(
          key: const ValueKey('portfolio-follow'),
          label: following ? 'Following' : 'Follow',
          filled: !following,
          onTap: onFollow,
        ),
      ),
      const SizedBox(width: 10),
      Expanded(
        child: _ActionButton(
          key: const ValueKey('portfolio-contact'),
          label: 'Contact',
          filled: false,
          onTap: onContact,
        ),
      ),
    ],
  );
}

class _OwnActions extends StatelessWidget {
  const _OwnActions({
    required this.onEdit,
    required this.onNewStory,
    required this.onSettings,
  });

  final VoidCallback onEdit;
  final VoidCallback onNewStory;
  final VoidCallback onSettings;

  @override
  Widget build(BuildContext context) => Row(
    children: [
      IconButton(
        key: const ValueKey('portfolio-settings'),
        tooltip: 'Settings',
        constraints: const BoxConstraints(minWidth: 48, minHeight: 48),
        onPressed: onSettings,
        icon: const Icon(Icons.settings_outlined, color: VelaColors.secondary),
      ),
      const SizedBox(width: 4),
      Expanded(
        child: _ActionButton(
          key: const ValueKey('portfolio-edit'),
          label: 'Edit profile',
          filled: false,
          onTap: onEdit,
        ),
      ),
      const SizedBox(width: 10),
      Expanded(
        child: _ActionButton(
          key: const ValueKey('portfolio-new-story'),
          label: 'New story',
          filled: true,
          onTap: onNewStory,
        ),
      ),
    ],
  );
}

class _ActionButton extends StatelessWidget {
  const _ActionButton({
    super.key,
    required this.label,
    required this.filled,
    required this.onTap,
  });

  final String label;
  final bool filled;
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
      child: Container(
        constraints: const BoxConstraints(minHeight: 48),
        alignment: Alignment.center,
        decoration: BoxDecoration(
          color: filled ? VelaColors.ink : Colors.transparent,
          borderRadius: BorderRadius.circular(12),
          border: filled ? null : Border.all(color: VelaColors.outline),
        ),
        child: Text(
          label,
          style: VelaText.sans14m.copyWith(
            color: filled ? VelaColors.background : VelaColors.ink,
          ),
        ),
      ),
    ),
  );
}
