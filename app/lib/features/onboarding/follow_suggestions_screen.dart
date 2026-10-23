import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:vela_api_client/vela_api_client.dart';

import '../../core/api/api_exception.dart';
import '../../core/theme/form_styles.dart';
import '../../core/theme/tokens.dart';
import '../portfolio/avatar.dart';
import 'onboarding_draft.dart';
import 'onboarding_submit.dart';
import 'onboarding_widgets.dart';

/// Follows needed so Following has something in it.
const minFollows = 3;

/// Step 2 of 2: pick at least three artists, or skip and use Discover.
/// Finishing sends the whole onboarding request.
class FollowSuggestionsScreen extends ConsumerStatefulWidget {
  const FollowSuggestionsScreen({super.key});

  @override
  ConsumerState<FollowSuggestionsScreen> createState() => _FollowState();
}

class _FollowState extends ConsumerState<FollowSuggestionsScreen> {
  bool _busy = false;
  String? _error;

  Future<void> _submit(Set<String> follows) async {
    if (_busy) return;
    setState(() {
      _busy = true;
      _error = null;
    });
    final messenger = ScaffoldMessenger.of(context);
    final router = GoRouter.of(context);
    final outcome = await ref.read(onboardingSubmitterProvider).submit(follows);
    if (!mounted) return;
    setState(() {
      _busy = false;
      _error = outcome.result == SubmitResult.failed
          ? _failureText(outcome.error)
          : null;
    });
    if (outcome.result != SubmitResult.done) return;
    if (outcome.avatarFailed) {
      messenger.showSnackBar(
        const SnackBar(
          content: Text(
            "You're all set. We couldn't upload your photo; you can add it "
            'from your profile.',
          ),
        ),
      );
    }
    router.go('/');
  }

  static String _failureText(ApiException? e) => switch (e?.code) {
    'network_error' => "Can't reach the server. Check your connection.",
    'rate_limited' => 'Too many tries. Wait a moment and try again.',
    'age_and_terms_required' =>
      'Confirm that you are 18 or older and accept the terms.',
    'account_suspended' => 'This account is suspended.',
    _ => 'Something went wrong. Try again.',
  };

  @override
  Widget build(BuildContext context) {
    final draft = ref.watch(onboardingDraftProvider);
    final suggestions = ref.watch(suggestedArtistsProvider);
    final list = suggestions.value;
    // With fewer than three suggestions, everyone on offer is enough.
    final needed = list == null
        ? minFollows
        : (list.length < minFollows ? list.length : minFollows);
    final enough = list != null && draft.follows.length >= needed;
    final selected = draft.follows.length;
    return StepLayout(
      kicker: 'STEP 2 OF 2',
      backKey: const ValueKey('follow-back'),
      onBack: _busy
          ? null
          : () => ref
                .read(onboardingDraftProvider.notifier)
                .goTo(OnboardingStep.profile),
      footer: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          if (_error != null)
            Padding(
              padding: const EdgeInsets.only(bottom: 10),
              child: Semantics(
                liveRegion: true,
                child: Text(
                  _error!,
                  key: const ValueKey('follow-error'),
                  style: VelaText.sans13.copyWith(color: VelaColors.danger),
                ),
              ),
            ),
          FilledButton(
            key: const ValueKey('follow-finish'),
            style: velaFilledButtonStyle().copyWith(
              minimumSize: const WidgetStatePropertyAll(Size(44, 52)),
            ),
            onPressed: enough && !_busy ? () => _submit(draft.follows) : null,
            child: Text(_busy ? 'Setting up…' : 'Finish'),
          ),
          TextButton(
            key: const ValueKey('follow-skip'),
            style: velaTextButtonStyle(),
            onPressed: _busy ? null : () => _submit(const {}),
            child: const Text('Skip and browse Discover instead'),
          ),
        ],
      ),
      children: [
        Semantics(
          header: true,
          child: Text('Follow a few artists', style: VelaText.serif36),
        ),
        const SizedBox(height: 8),
        Text(
          'Pick at least $minFollows to fill your Following feed. Skip it and '
          'Following shows Discover until you follow someone.',
          style: VelaText.sans14.copyWith(color: VelaColors.secondary),
        ),
        const SizedBox(height: 14),
        Semantics(
          liveRegion: true,
          child: Text(
            '$selected of $needed selected',
            key: const ValueKey('follow-count'),
            style: VelaText.monoKicker,
          ),
        ),
        const SizedBox(height: 10),
        ...suggestions.when(
          loading: () => const [_Loading()],
          error: (_, _) => [
            Text("Couldn't load suggestions.", style: VelaText.sans14),
            TextButton(
              key: const ValueKey('follow-retry'),
              style: velaTextButtonStyle(),
              onPressed: () => ref.invalidate(suggestedArtistsProvider),
              child: const Text('Retry'),
            ),
          ],
          data: (artists) => [
            if (artists.isEmpty)
              Text(
                'No suggestions yet. Discover has plenty to look at.',
                style: VelaText.sans14.copyWith(color: VelaColors.muted),
              ),
            for (final a in artists)
              _ArtistRow(
                artist: a,
                selected: draft.follows.contains(a.id),
                enabled: !_busy,
                onTap: () => ref
                    .read(onboardingDraftProvider.notifier)
                    .toggleFollow(a.id),
              ),
          ],
        ),
      ],
    );
  }
}

class _Loading extends StatelessWidget {
  const _Loading();

  @override
  Widget build(BuildContext context) => const Padding(
    padding: EdgeInsets.symmetric(vertical: 32),
    child: Center(
      child: CircularProgressIndicator(
        strokeWidth: 2,
        color: VelaColors.accent,
      ),
    ),
  );
}

class _ArtistRow extends StatelessWidget {
  const _ArtistRow({
    required this.artist,
    required this.selected,
    required this.enabled,
    required this.onTap,
  });

  final Profile artist;
  final bool selected;
  final bool enabled;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final detail = [
      '@${artist.handle}',
      if ((artist.discipline ?? '').isNotEmpty) artist.discipline!,
    ].join(' · ');
    return Semantics(
      button: true,
      toggled: selected,
      label: '${selected ? 'Following' : 'Follow'} ${artist.displayName}',
      excludeSemantics: true,
      child: InkWell(
        key: ValueKey('artist-${artist.id}'),
        onTap: enabled ? onTap : null,
        borderRadius: BorderRadius.circular(12),
        child: ConstrainedBox(
          constraints: const BoxConstraints(minHeight: 64),
          child: Padding(
            padding: const EdgeInsets.symmetric(vertical: 8),
            child: Row(
              children: [
                Avatar(
                  name: artist.displayName,
                  url: artist.avatarUrl,
                  size: 40,
                ),
                const SizedBox(width: 12),
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(
                        artist.displayName,
                        maxLines: 1,
                        overflow: TextOverflow.ellipsis,
                        style: VelaText.sans14m,
                      ),
                      Text(
                        detail,
                        maxLines: 1,
                        overflow: TextOverflow.ellipsis,
                        style: VelaText.mono11,
                      ),
                    ],
                  ),
                ),
                const SizedBox(width: 12),
                _Pill(selected: selected),
              ],
            ),
          ),
        ),
      ),
    );
  }
}

/// The Follow pill from the handoff (outline, 99 radius, Geist 500 12).
class _Pill extends StatelessWidget {
  const _Pill({required this.selected});

  final bool selected;

  @override
  Widget build(BuildContext context) => AnimatedContainer(
    duration: const Duration(milliseconds: 150),
    padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 6),
    decoration: BoxDecoration(
      color: selected ? VelaColors.ink : Colors.transparent,
      borderRadius: BorderRadius.circular(99),
      border: Border.all(color: selected ? VelaColors.ink : VelaColors.outline),
    ),
    child: Text(
      selected ? 'Following' : 'Follow',
      style: VelaText.sans12m.copyWith(
        color: selected ? VelaColors.background : VelaColors.ink,
      ),
    ),
  );
}
