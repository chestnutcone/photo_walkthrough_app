import 'package:flutter/material.dart';

import '../../core/api/api_exception.dart';
import '../../core/theme/tokens.dart';

/// Placeholder cards while the first page loads. Static on purpose: no
/// endless animation to fight reduced-motion settings.
class FeedSkeleton extends StatelessWidget {
  const FeedSkeleton({super.key});

  @override
  Widget build(BuildContext context) {
    Widget block(double h, {double? w, double radius = 6}) => Container(
      height: h,
      width: w,
      decoration: BoxDecoration(
        color: VelaColors.input,
        borderRadius: BorderRadius.circular(radius),
      ),
    );
    return Semantics(
      label: 'Loading feed',
      child: ExcludeSemantics(
        child: Padding(
          padding: const EdgeInsets.symmetric(horizontal: 16),
          child: Column(
            key: const ValueKey('feed-skeleton'),
            children: [
              for (var i = 0; i < 2; i++) ...[
                Row(
                  children: [
                    Container(
                      width: 34,
                      height: 34,
                      decoration: const BoxDecoration(
                        color: VelaColors.input,
                        shape: BoxShape.circle,
                      ),
                    ),
                    const SizedBox(width: 10),
                    block(14, w: 120),
                  ],
                ),
                const SizedBox(height: 12),
                AspectRatio(
                  aspectRatio: 358 / 448,
                  child: block(double.infinity, radius: 14),
                ),
                const SizedBox(height: 12),
                block(28, w: 200),
                const SizedBox(height: 36),
              ],
            ],
          ),
        ),
      ),
    );
  }
}

/// Centered message with an optional action, used for empty/error states.
class FeedMessage extends StatelessWidget {
  const FeedMessage({
    super.key,
    required this.title,
    required this.body,
    this.actionLabel,
    this.actionKey,
    this.onAction,
  });

  final String title;
  final String body;
  final String? actionLabel;
  final Key? actionKey;
  final VoidCallback? onAction;

  @override
  Widget build(BuildContext context) => Padding(
    padding: const EdgeInsets.symmetric(horizontal: 32, vertical: 48),
    child: Column(
      mainAxisSize: MainAxisSize.min,
      children: [
        Text(title, style: VelaText.serif28, textAlign: TextAlign.center),
        const SizedBox(height: 10),
        Text(
          body,
          style: VelaText.sans14.copyWith(color: VelaColors.muted),
          textAlign: TextAlign.center,
        ),
        if (actionLabel != null) ...[
          const SizedBox(height: 20),
          TextButton(
            key: actionKey,
            onPressed: onAction,
            style: TextButton.styleFrom(
              minimumSize: const Size(44, 44),
              foregroundColor: VelaColors.background,
              backgroundColor: VelaColors.ink,
              padding: const EdgeInsets.symmetric(horizontal: 20),
              shape: const StadiumBorder(),
            ),
            child: Text(
              actionLabel!,
              style: VelaText.sans13s.copyWith(color: VelaColors.background),
            ),
          ),
        ],
      ],
    ),
  );
}

bool isOffline(ApiException e) => e.code == 'network_error';

/// Full-feed failure: offline wording for transport errors, generic otherwise.
class FeedErrorMessage extends StatelessWidget {
  const FeedErrorMessage({
    super.key,
    required this.error,
    required this.onRetry,
  });

  final ApiException error;
  final VoidCallback onRetry;

  @override
  Widget build(BuildContext context) => FeedMessage(
    title: isOffline(error) ? "You're offline" : "Couldn't load the feed",
    body: isOffline(error)
        ? 'Check your connection and try again.'
        : 'Something went wrong on our side. Try again in a moment.',
    actionLabel: 'Retry',
    actionKey: const ValueKey('feed-retry'),
    onAction: onRetry,
  );
}

/// Footer under the list: spinner while loading a page, retry row on failure.
class FeedMoreFooter extends StatelessWidget {
  const FeedMoreFooter({
    super.key,
    required this.loading,
    required this.error,
    required this.onRetry,
  });

  final bool loading;
  final ApiException? error;
  final VoidCallback onRetry;

  @override
  Widget build(BuildContext context) {
    if (error != null) {
      return Padding(
        padding: const EdgeInsets.symmetric(vertical: 12),
        child: Row(
          mainAxisAlignment: MainAxisAlignment.center,
          children: [
            Text(
              isOffline(error!) ? "You're offline" : "Couldn't load more",
              style: VelaText.sans13.copyWith(color: VelaColors.muted),
            ),
            TextButton(
              key: const ValueKey('more-retry'),
              onPressed: onRetry,
              style: TextButton.styleFrom(minimumSize: const Size(44, 44)),
              child: Text(
                'Retry',
                style: VelaText.sans13m.copyWith(color: VelaColors.accent),
              ),
            ),
          ],
        ),
      );
    }
    if (loading) {
      return const Padding(
        padding: EdgeInsets.symmetric(vertical: 20),
        child: Center(
          child: SizedBox(
            width: 20,
            height: 20,
            child: CircularProgressIndicator(
              strokeWidth: 2,
              color: VelaColors.accent,
            ),
          ),
        ),
      );
    }
    return const SizedBox.shrink();
  }
}
