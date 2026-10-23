import 'dart:ui' show ImageFilter;

import 'package:flutter/material.dart';
import 'package:vela_api_client/vela_api_client.dart';

import '../../../core/theme/tokens.dart';
import '../story_controller.dart';

const _months = [
  'JAN',
  'FEB',
  'MAR',
  'APR',
  'MAY',
  'JUN',
  'JUL',
  'AUG',
  'SEP',
  'OCT',
  'NOV',
  'DEC',
];

/// "OCT 2026" from the publish (or creation) date, in UTC.
String storyDateLabel(Work work) {
  final d = (work.publishedAt ?? work.createdAt).toUtc();
  return '${_months[d.month - 1]} ${d.year}';
}

String _count(int n) => '$n CHAPTER${n == 1 ? '' : 'S'}';

String _replyLabel(int? n) => switch (n ?? 0) {
  0 => 'Reply',
  1 => '1 reply',
  final c => '$c replies',
};

/// One action on the end card.
class EndAction {
  const EndAction({
    required this.label,
    required this.onTap,
    this.filled = false,
    this.fullWidth = false,
  });

  final String label;
  final VoidCallback onTap;
  final bool filled;
  final bool fullWidth;
}

/// The frosted card at the bottom of the viewer. Shows the intro, the current
/// chapter, the end card or the Explore hint depending on state.
class StoryBottomCard extends StatelessWidget {
  const StoryBottomCard({
    super.key,
    required this.work,
    required this.state,
    required this.endActions,
    required this.onBegin,
    required this.onZoomOut,
    required this.onReplies,
    required this.cardKey,
  });

  final Work work;
  final StoryState state;
  final List<EndAction> endActions;
  final VoidCallback onBegin;
  final VoidCallback onZoomOut;
  final void Function(int chapterIndex) onReplies;
  final GlobalKey cardKey;

  @override
  Widget build(BuildContext context) {
    final n = work.chapters.length;
    final chapter = state.chapterIndex(n);
    final explore = state.mode == StoryMode.explore;
    final maxHeight = MediaQuery.sizeOf(context).height * 0.6;
    final Widget content;
    if (chapter >= 0) {
      content = _ChapterBody(
        chapter: work.chapters[chapter],
        index: chapter,
        total: n,
        showZoomOut: explore,
        onZoomOut: onZoomOut,
        onReplies: () => onReplies(chapter),
      );
    } else if (explore) {
      final first = work.author.displayName.split(' ').first;
      content = _ExploreIdleBody(count: n, first: first);
    } else if (state.step == 0) {
      content = _IntroBody(work: work, onBegin: onBegin);
    } else {
      content = _EndBody(actions: endActions);
    }
    return Positioned(
      left: 10,
      right: 10,
      bottom: 24,
      child: Semantics(
        key: const ValueKey('bottom-card'),
        liveRegion: true,
        container: true,
        child: ClipRRect(
          key: cardKey,
          borderRadius: BorderRadius.circular(24),
          child: BackdropFilter(
            filter: ImageFilter.blur(sigmaX: 18, sigmaY: 18),
            child: Container(
              constraints: BoxConstraints(maxHeight: maxHeight),
              padding: const EdgeInsets.fromLTRB(20, 20, 20, 16),
              decoration: BoxDecoration(
                color: const Color(0xDB12110F),
                borderRadius: BorderRadius.circular(24),
                border: Border.all(color: const Color(0x1AF2EEE6)),
              ),
              child: SingleChildScrollView(child: content),
            ),
          ),
        ),
      ),
    );
  }
}

class _Kicker extends StatelessWidget {
  const _Kicker(this.text);
  final String text;

  @override
  Widget build(BuildContext context) => Text(text, style: VelaText.monoKicker);
}

TextStyle get _body => VelaText.sans15.copyWith(color: VelaColors.secondary);

class _IntroBody extends StatelessWidget {
  const _IntroBody({required this.work, required this.onBegin});

  final Work work;
  final VoidCallback onBegin;

  @override
  Widget build(BuildContext context) => Column(
    crossAxisAlignment: CrossAxisAlignment.start,
    mainAxisSize: MainAxisSize.min,
    spacing: 10,
    children: [
      _Kicker('${storyDateLabel(work)} · ${_count(work.chapters.length)}'),
      Text(work.title, style: VelaText.serif34.copyWith(letterSpacing: -0.3)),
      if ((work.intro ?? '').isNotEmpty) Text(work.intro!, style: _body),
      Padding(
        padding: const EdgeInsets.only(top: 6),
        child: Row(
          mainAxisAlignment: MainAxisAlignment.spaceBetween,
          children: [
            Flexible(
              child: Text(
                'Tap the photo to walk through',
                style: VelaText.sans12.copyWith(color: VelaColors.muted),
              ),
            ),
            const SizedBox(width: 8),
            GestureDetector(
              onTap: onBegin,
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
                  'Begin →',
                  style: VelaText.sans13s.copyWith(
                    color: VelaColors.background,
                  ),
                ),
              ),
            ),
          ],
        ),
      ),
    ],
  );
}

class _ChapterBody extends StatelessWidget {
  const _ChapterBody({
    required this.chapter,
    required this.index,
    required this.total,
    required this.showZoomOut,
    required this.onZoomOut,
    required this.onReplies,
  });

  final Chapter chapter;
  final int index;
  final int total;
  final bool showZoomOut;
  final VoidCallback onZoomOut;
  final VoidCallback onReplies;

  String _two(int v) => v.toString().padLeft(2, '0');

  @override
  Widget build(BuildContext context) => Column(
    crossAxisAlignment: CrossAxisAlignment.start,
    mainAxisSize: MainAxisSize.min,
    spacing: 10,
    children: [
      Row(
        mainAxisAlignment: MainAxisAlignment.spaceBetween,
        children: [
          _Kicker('CHAPTER ${_two(index + 1)} / ${_two(total)}'),
          if (showZoomOut)
            GestureDetector(
              onTap: onZoomOut,
              behavior: HitTestBehavior.opaque,
              child: Text(
                'Zoom out',
                style: VelaText.sans12m.copyWith(color: VelaColors.muted),
              ),
            ),
        ],
      ),
      Text(
        chapter.title,
        style: VelaText.serif32.copyWith(letterSpacing: -0.3),
      ),
      Text(chapter.body, style: _body),
      Padding(
        padding: const EdgeInsets.only(top: 4),
        child: Row(
          spacing: 8,
          children: [
            if ((chapter.meta ?? '').isNotEmpty)
              Flexible(
                child: Container(
                  padding: const EdgeInsets.symmetric(
                    horizontal: 8,
                    vertical: 5,
                  ),
                  decoration: BoxDecoration(
                    borderRadius: BorderRadius.circular(6),
                    border: Border.all(color: const Color(0x24F2EEE6)),
                  ),
                  child: Text(
                    chapter.meta!,
                    style: VelaText.mono11.copyWith(color: VelaColors.muted),
                  ),
                ),
              ),
            const Spacer(),
            GestureDetector(
              onTap: onReplies,
              behavior: HitTestBehavior.opaque,
              child: Container(
                padding: const EdgeInsets.symmetric(
                  horizontal: 13,
                  vertical: 8,
                ),
                decoration: BoxDecoration(
                  color: const Color(0x14F2EEE6),
                  borderRadius: BorderRadius.circular(99),
                ),
                child: Text(
                  _replyLabel(chapter.replyCount),
                  style: VelaText.sans12m,
                ),
              ),
            ),
          ],
        ),
      ),
    ],
  );
}

class _ExploreIdleBody extends StatelessWidget {
  const _ExploreIdleBody({required this.count, required this.first});

  final int count;
  final String first;

  @override
  Widget build(BuildContext context) => Column(
    crossAxisAlignment: CrossAxisAlignment.start,
    mainAxisSize: MainAxisSize.min,
    spacing: 10,
    children: [
      const _Kicker('EXPLORE'),
      SizedBox(
        width: double.infinity,
        child: Text(
          'Tap any marked point in any order. $count notes left by $first.',
          style: _body,
        ),
      ),
    ],
  );
}

class _EndBody extends StatelessWidget {
  const _EndBody({required this.actions});

  final List<EndAction> actions;

  @override
  Widget build(BuildContext context) {
    final half = actions.where((a) => !a.fullWidth).toList();
    final full = actions.where((a) => a.fullWidth).toList();
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      mainAxisSize: MainAxisSize.min,
      spacing: 10,
      children: [
        const _Kicker('END OF STORY'),
        Text('Keep looking, or move on.', style: VelaText.serif30),
        // 10pt column gap + 6pt grid margin = 16pt before the buttons.
        Padding(
          padding: const EdgeInsets.only(top: 6),
          child: Column(
            spacing: 8,
            children: [
              for (var i = 0; i < half.length; i += 2)
                Row(
                  spacing: 8,
                  children: [
                    Expanded(child: _EndButton(half[i])),
                    if (i + 1 < half.length)
                      Expanded(child: _EndButton(half[i + 1])),
                  ],
                ),
              for (final a in full)
                SizedBox(width: double.infinity, child: _EndButton(a)),
            ],
          ),
        ),
      ],
    );
  }
}

class _EndButton extends StatelessWidget {
  const _EndButton(this.action);

  final EndAction action;

  @override
  Widget build(BuildContext context) => Semantics(
    button: true,
    excludeSemantics: true,
    label: action.label,
    onTap: action.onTap,
    child: GestureDetector(
      onTap: action.onTap,
      behavior: HitTestBehavior.opaque,
      child: Container(
        alignment: Alignment.center,
        padding: const EdgeInsets.all(12),
        decoration: BoxDecoration(
          color: action.filled ? VelaColors.ink : null,
          borderRadius: BorderRadius.circular(12),
          border: action.filled
              ? null
              : Border.all(color: const Color(0x33F2EEE6)),
        ),
        child: Text(
          action.label,
          textAlign: TextAlign.center,
          style: (action.filled ? VelaText.sans13s : VelaText.sans13m).copyWith(
            color: action.filled ? VelaColors.background : VelaColors.ink,
          ),
        ),
      ),
    ),
  );
}
