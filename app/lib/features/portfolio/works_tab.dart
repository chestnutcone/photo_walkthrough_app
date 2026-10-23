import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:vela_api_client/vela_api_client.dart';

import '../../core/theme/tokens.dart';
import '../feed/feed_format.dart';
import '../story/photo_layout.dart';
import '../story/story_settings.dart';
import 'dashed_box.dart';
import 'paged.dart';
import 'portfolio_data.dart';

const double _gridGap = 8;

/// The works tab as slivers: featured story, then the grid of the rest.
class WorksSlivers extends ConsumerWidget {
  const WorksSlivers({super.key, required this.handle, required this.isOwn});

  final String handle;
  final bool isOwn;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final state = ref.watch(worksControllerProvider(handle));
    final ctl = ref.read(worksControllerProvider(handle).notifier);
    if (state.isLoading) {
      return const SliverToBoxAdapter(child: _WorksSkeleton());
    }
    if (state.error != null) {
      return SliverToBoxAdapter(
        child: _Message(
          text: "Couldn't load the works.",
          action: 'Retry',
          actionKey: const ValueKey('works-retry'),
          onAction: ctl.reload,
        ),
      );
    }
    if (state.items.isEmpty) {
      return SliverToBoxAdapter(child: _EmptyWorks(isOwn: isOwn));
    }
    final featured = state.items.first;
    final rest = state.items.skip(1).toList();
    return SliverMainAxisGroup(
      slivers: [
        SliverPadding(
          padding: const EdgeInsets.fromLTRB(16, 18, 16, 0),
          sliver: SliverToBoxAdapter(child: _Featured(work: featured)),
        ),
        if (rest.isNotEmpty)
          SliverPadding(
            padding: const EdgeInsets.fromLTRB(16, 20, 16, 10),
            sliver: SliverToBoxAdapter(
              child: Text('ALL WORKS', style: VelaText.mono11),
            ),
          ),
        SliverPadding(
          padding: const EdgeInsets.symmetric(horizontal: 16),
          sliver: SliverGrid.builder(
            gridDelegate: const SliverGridDelegateWithFixedCrossAxisCount(
              crossAxisCount: 2,
              mainAxisSpacing: _gridGap,
              crossAxisSpacing: _gridGap,
              childAspectRatio: 4 / 5,
            ),
            itemCount: rest.length,
            itemBuilder: (_, i) => _WorkTile(work: rest[i]),
          ),
        ),
        SliverToBoxAdapter(
          child: _Footer(state: state, onRetry: ctl.retryMore),
        ),
      ],
    );
  }
}

class _Featured extends ConsumerWidget {
  const _Featured({required this.work});
  final Work work;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    void open() => context.push('/story/${work.id}');
    return Column(
      key: const ValueKey('featured-story'),
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text('FEATURED STORY', style: VelaText.monoKicker),
        const SizedBox(height: 10),
        Semantics(
          button: true,
          image: true,
          label: work.altText ?? 'Photograph: ${work.title}',
          excludeSemantics: true,
          onTap: open,
          child: GestureDetector(
            onTap: open,
            child: AspectRatio(
              aspectRatio: 358 / 300,
              child: ClipRRect(
                borderRadius: BorderRadius.circular(14),
                child: _PinnedPhoto(work: work),
              ),
            ),
          ),
        ),
        const SizedBox(height: 10),
        Row(
          crossAxisAlignment: CrossAxisAlignment.end,
          children: [
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    work.title,
                    style: VelaText.serif26,
                    maxLines: 2,
                    overflow: TextOverflow.ellipsis,
                  ),
                  const SizedBox(height: 4),
                  Text(
                    feedMeta(work),
                    style: VelaText.mono11.copyWith(color: VelaColors.muted),
                  ),
                ],
              ),
            ),
            const SizedBox(width: 12),
            Semantics(
              button: true,
              label: 'Walk through ${work.title}',
              excludeSemantics: true,
              onTap: open,
              child: GestureDetector(
                onTap: open,
                behavior: HitTestBehavior.opaque,
                child: ConstrainedBox(
                  constraints: const BoxConstraints(minHeight: 44),
                  child: Center(
                    child: Container(
                      padding: const EdgeInsets.symmetric(
                        horizontal: 16,
                        vertical: 10,
                      ),
                      decoration: BoxDecoration(
                        color: VelaColors.ink,
                        borderRadius: BorderRadius.circular(99),
                      ),
                      child: Text(
                        'Walk through',
                        style: VelaText.sans13s.copyWith(
                          color: VelaColors.background,
                        ),
                      ),
                    ),
                  ),
                ),
              ),
            ),
          ],
        ),
      ],
    );
  }
}

/// Photo with the chapter pins drawn as accent dots.
class _PinnedPhoto extends ConsumerWidget {
  const _PinnedPhoto({required this.work});
  final Work work;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final url = workImageUrl(work);
    final factory = ref.watch(storyImageProviderFactoryProvider);
    final w = work.imageWidth, h = work.imageHeight;
    final aspect = (w != null && h != null && w > 0 && h > 0) ? w / h : null;
    final pins = [for (final c in work.chapters) Offset(c.x, c.y)];
    return ColoredBox(
      color: VelaColors.input,
      child: LayoutBuilder(
        builder: (context, box) {
          final size = box.biggest;
          final frame = computePhotoFrame(
            viewport: size,
            aspect: aspect,
            pins: pins,
          );
          return Stack(
            fit: StackFit.expand,
            children: [
              if (url != null)
                Positioned.fromRect(
                  rect: frame.rect,
                  child: Image(
                    image: factory(url),
                    fit: BoxFit.cover,
                    errorBuilder: (_, _, _) => const Center(
                      child: Icon(
                        Icons.broken_image_outlined,
                        color: VelaColors.faint,
                      ),
                    ),
                  ),
                ),
              for (final pin in pins)
                Positioned(
                  left: frame.toLayer(pin).dx * size.width - 8,
                  top: frame.toLayer(pin).dy * size.height - 8,
                  width: 16,
                  height: 16,
                  child: const ExcludeSemantics(child: _Dot()),
                ),
            ],
          );
        },
      ),
    );
  }
}

class _Dot extends StatelessWidget {
  const _Dot();

  @override
  Widget build(BuildContext context) => DecoratedBox(
    decoration: BoxDecoration(
      shape: BoxShape.circle,
      color: VelaColors.accent.withValues(alpha: 0.25),
    ),
    child: const Padding(
      padding: EdgeInsets.all(3),
      child: DecoratedBox(
        decoration: BoxDecoration(
          shape: BoxShape.circle,
          color: VelaColors.accent,
        ),
      ),
    ),
  );
}

class _WorkTile extends ConsumerWidget {
  const _WorkTile({required this.work});
  final Work work;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final url = workImageUrl(work) ?? work.thumbUrl;
    final n = work.chapters.length;
    final chapters = '$n ${n == 1 ? 'CHAPTER' : 'CHAPTERS'}';
    void open() => context.push('/story/${work.id}');
    return Semantics(
      button: true,
      label: '${work.title}, $n ${n == 1 ? 'chapter' : 'chapters'}',
      excludeSemantics: true,
      onTap: open,
      child: GestureDetector(
        key: ValueKey('work-tile-${work.id}'),
        onTap: open,
        child: ClipRRect(
          borderRadius: BorderRadius.circular(10),
          child: Stack(
            fit: StackFit.expand,
            children: [
              const ColoredBox(color: VelaColors.input),
              if (url != null)
                Image(
                  image: ref.watch(storyImageProviderFactoryProvider)(url),
                  fit: BoxFit.cover,
                  errorBuilder: (_, _, _) => const SizedBox.shrink(),
                ),
              Positioned(
                left: 0,
                right: 0,
                bottom: 0,
                child: Container(
                  padding: const EdgeInsets.fromLTRB(10, 28, 10, 10),
                  decoration: const BoxDecoration(
                    gradient: LinearGradient(
                      begin: Alignment.topCenter,
                      end: Alignment.bottomCenter,
                      colors: [Color(0x00000000), Color(0x99000000)],
                    ),
                  ),
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    mainAxisSize: MainAxisSize.min,
                    children: [
                      Text(
                        work.title,
                        style: VelaText.serif18.copyWith(color: Colors.white),
                        maxLines: 2,
                        overflow: TextOverflow.ellipsis,
                      ),
                      Text(
                        chapters,
                        style: VelaText.mono10.copyWith(
                          color: const Color(0xBFFFFFFF),
                        ),
                      ),
                    ],
                  ),
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

class _Footer extends StatelessWidget {
  const _Footer({required this.state, required this.onRetry});
  final PagedState<Work> state;
  final VoidCallback onRetry;

  @override
  Widget build(BuildContext context) {
    if (state.loadingMore) {
      return const Padding(
        padding: EdgeInsets.all(24),
        child: Center(
          child: SizedBox(
            width: 20,
            height: 20,
            child: CircularProgressIndicator(strokeWidth: 2),
          ),
        ),
      );
    }
    if (state.moreError != null) {
      return _Message(
        text: "Couldn't load more.",
        action: 'Retry',
        actionKey: const ValueKey('works-more-retry'),
        onAction: onRetry,
      );
    }
    return const SizedBox(height: 32);
  }
}

class _EmptyWorks extends ConsumerWidget {
  const _EmptyWorks({required this.isOwn});
  final bool isOwn;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final me = isOwn ? ref.watch(meProvider).value : null;
    return Padding(
      padding: const EdgeInsets.fromLTRB(16, 18, 16, 32),
      child: DashedBox(
        key: const ValueKey('works-empty'),
        color: VelaColors.outline,
        padding: const EdgeInsets.symmetric(vertical: 40, horizontal: 20),
        child: Column(
          children: [
            Text(
              isOwn
                  ? 'No stories yet. Tap + to annotate your first photograph.'
                  : 'No stories yet.',
              textAlign: TextAlign.center,
              style: VelaText.sans14.copyWith(color: VelaColors.muted),
            ),
            if (me != null) ...[
              const SizedBox(height: 12),
              Text(
                '${me.usedWorks} of ${me.tier.maxWorks} stories',
                style: VelaText.mono11,
              ),
            ],
          ],
        ),
      ),
    );
  }
}

class _WorksSkeleton extends StatelessWidget {
  const _WorksSkeleton();

  @override
  Widget build(BuildContext context) => Semantics(
    label: 'Loading works',
    child: const Padding(
      padding: EdgeInsets.all(16),
      child: AspectRatio(
        aspectRatio: 358 / 300,
        child: DecoratedBox(
          decoration: BoxDecoration(
            color: VelaColors.input,
            borderRadius: BorderRadius.all(Radius.circular(14)),
          ),
        ),
      ),
    ),
  );
}

class _Message extends StatelessWidget {
  const _Message({
    required this.text,
    required this.action,
    required this.actionKey,
    required this.onAction,
  });

  final String text;
  final String action;
  final Key actionKey;
  final VoidCallback onAction;

  @override
  Widget build(BuildContext context) => Padding(
    padding: const EdgeInsets.all(24),
    child: Column(
      children: [
        Text(text, style: VelaText.sans14.copyWith(color: VelaColors.muted)),
        TextButton(
          key: actionKey,
          onPressed: onAction,
          style: TextButton.styleFrom(minimumSize: const Size(44, 44)),
          child: Text(action),
        ),
      ],
    ),
  );
}
