import 'dart:math' as math;

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:vela_api_client/vela_api_client.dart';

import '../../core/theme/tokens.dart';
import '../report/report_sheet_hook.dart';
import 'replies_controller.dart';
import 'reply_composer.dart';
import 'reply_tile.dart';

const double _sheetHeight = 520;
const double _loadMoreExtent = 240;

/// Opens the replies sheet for one chapter. The future completes when the
/// sheet is dismissed (Done, scrim tap or drag).
Future<void> showRepliesSheet(
  BuildContext context, {
  required String workId,
  required int chapterIndex,
  required String chapterTitle,
  bool readOnly = false,
}) => showModalBottomSheet<void>(
  context: context,
  isScrollControlled: true,
  useSafeArea: true,
  backgroundColor: VelaColors.sheet,
  barrierColor: const Color(0x66000000),
  shape: const RoundedRectangleBorder(
    borderRadius: BorderRadius.vertical(top: Radius.circular(26)),
  ),
  builder: (_) => RepliesSheet(
    workId: workId,
    chapterIndex: chapterIndex,
    chapterTitle: chapterTitle,
    readOnly: readOnly,
  ),
);

class RepliesSheet extends ConsumerWidget {
  const RepliesSheet({
    required this.workId,
    required this.chapterIndex,
    required this.chapterTitle,
    required this.readOnly,
    super.key,
  });

  final String workId;
  final int chapterIndex;
  final String chapterTitle;
  final bool readOnly;

  RepliesKey get _key => (workId: workId, chapterIndex: chapterIndex);

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final media = MediaQuery.of(context);
    final keyboard = media.viewInsets.bottom;
    // Shrinks with the keyboard so the composer stays on screen.
    final height = math.min(
      _sheetHeight,
      math.max(220.0, media.size.height - keyboard - 72),
    );
    final bottomSafe = keyboard > 0 ? 0.0 : media.padding.bottom;
    return AnimatedPadding(
      duration: const Duration(milliseconds: 100),
      padding: EdgeInsets.only(bottom: keyboard),
      child: SizedBox(
        height: height,
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            const _Grabber(),
            _Header(title: chapterTitle),
            Expanded(
              child: _Body(keyArg: _key, readOnly: readOnly),
            ),
            Padding(
              padding: EdgeInsets.fromLTRB(16, 8, 16, 12 + bottomSafe),
              child: readOnly
                  ? Text('Replies can be read here.', style: VelaText.mono11)
                  : ReplyComposer(keyArg: _key),
            ),
          ],
        ),
      ),
    );
  }
}

class _Grabber extends StatelessWidget {
  const _Grabber();

  @override
  Widget build(BuildContext context) => Padding(
    padding: const EdgeInsets.only(top: 10, bottom: 6),
    child: Center(
      child: Container(
        key: const ValueKey('sheet-grabber'),
        width: 36,
        height: 4,
        decoration: BoxDecoration(
          color: VelaColors.faint,
          borderRadius: BorderRadius.circular(2),
        ),
      ),
    ),
  );
}

class _Header extends StatelessWidget {
  const _Header({required this.title});
  final String title;

  @override
  Widget build(BuildContext context) => Padding(
    padding: const EdgeInsets.fromLTRB(20, 4, 8, 8),
    child: Row(
      crossAxisAlignment: CrossAxisAlignment.center,
      children: [
        Expanded(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text('REPLIES ON', style: VelaText.mono11),
              Text(
                title,
                maxLines: 2,
                overflow: TextOverflow.ellipsis,
                style: VelaText.serif24,
              ),
            ],
          ),
        ),
        TextButton(
          onPressed: () => Navigator.of(context).pop(),
          child: Semantics(
            label: 'Close replies',
            excludeSemantics: true,
            child: Text('Done', style: VelaText.sans13m),
          ),
        ),
      ],
    ),
  );
}

class _Body extends ConsumerStatefulWidget {
  const _Body({required this.keyArg, required this.readOnly});
  final RepliesKey keyArg;
  final bool readOnly;

  @override
  ConsumerState<_Body> createState() => _BodyState();
}

class _BodyState extends ConsumerState<_Body> {
  RepliesController get _ctl =>
      ref.read(repliesControllerProvider(widget.keyArg).notifier);

  final _scroll = ScrollController();

  @override
  void dispose() {
    _scroll.dispose();
    super.dispose();
  }

  /// A first page that does not fill the viewport never scrolls, so ask for
  /// the next page here.
  void _fillViewport() {
    if (!mounted || !_scroll.hasClients) return;
    final state = ref.read(repliesControllerProvider(widget.keyArg));
    if (state.canLoadMore && _scroll.position.extentAfter < _loadMoreExtent) {
      _ctl.loadMore();
    }
  }

  bool _onScroll(ScrollNotification n) {
    if (n.metrics.extentAfter < _loadMoreExtent) _ctl.loadMore();
    return false;
  }

  Future<void> _confirmDelete(Reply r) async {
    final ok = await showDialog<bool>(
      context: context,
      builder: (ctx) => AlertDialog(
        backgroundColor: VelaColors.sheet,
        title: Text('Delete this reply?', style: VelaText.serif24),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(ctx, false),
            child: const Text('Cancel'),
          ),
          TextButton(
            onPressed: () => Navigator.pop(ctx, true),
            child: const Text('Delete'),
          ),
        ],
      ),
    );
    if (ok == true && mounted) await _ctl.delete(r.id);
  }

  Future<void> _report(Reply r) async {
    final filed = await ref.read(reportSheetHook)(
      context,
      targetType: 'reply',
      targetId: r.id,
    );
    if (filed && mounted) _ctl.hideLocally(r.id);
  }

  @override
  Widget build(BuildContext context) {
    final state = ref.watch(repliesControllerProvider(widget.keyArg));
    final viewerAsync = ref.watch(viewerIdsProvider);
    final viewer = viewerAsync.value ?? const <String>{};
    final viewerKnown = viewerAsync.hasValue || viewerAsync.hasError;
    WidgetsBinding.instance.addPostFrameCallback((_) => _fillViewport());
    if (state.isLoading) {
      return const Center(
        child: SizedBox(
          width: 22,
          height: 22,
          child: CircularProgressIndicator(
            strokeWidth: 2,
            color: VelaColors.accent,
          ),
        ),
      );
    }
    if (state.error != null) {
      return _Message(text: 'Could not load replies.', onRetry: _ctl.refresh);
    }
    if (state.items.isEmpty) {
      return Center(
        child: Text(
          'No replies on this detail yet.',
          style: VelaText.sans14.copyWith(color: VelaColors.faint),
        ),
      );
    }
    return Column(
      children: [
        Expanded(
          child: NotificationListener<ScrollNotification>(
            onNotification: _onScroll,
            child: ListView.builder(
              controller: _scroll,
              padding: const EdgeInsets.symmetric(horizontal: 20),
              itemCount: state.items.length + 1,
              itemBuilder: (context, i) {
                if (i == state.items.length) {
                  return _Footer(state: state, keyArg: widget.keyArg);
                }
                final r = state.items[i];
                return ReplyTile(
                  key: ValueKey(r.id),
                  reply: r,
                  isOwn:
                      viewer.contains(r.author.id) ||
                      state.ownIds.contains(r.id),
                  showActions: viewerKnown || state.ownIds.contains(r.id),
                  readOnly: widget.readOnly,
                  onAction: (a) =>
                      a == ReplyAction.delete ? _confirmDelete(r) : _report(r),
                );
              },
            ),
          ),
        ),
        if (state.actionError != null)
          Padding(
            padding: const EdgeInsets.fromLTRB(20, 4, 20, 0),
            child: Semantics(
              liveRegion: true,
              child: Text(
                'Could not delete the reply.',
                style: VelaText.sans12.copyWith(color: const Color(0xFFE59A8A)),
              ),
            ),
          ),
      ],
    );
  }
}

class _Footer extends ConsumerWidget {
  const _Footer({required this.state, required this.keyArg});
  final RepliesState state;
  final RepliesKey keyArg;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    if (state.loadingMore) {
      return const Padding(
        padding: EdgeInsets.all(16),
        child: Center(
          child: SizedBox(
            width: 18,
            height: 18,
            child: CircularProgressIndicator(strokeWidth: 2),
          ),
        ),
      );
    }
    if (state.moreError != null) {
      return _Message(
        text: 'Could not load more replies.',
        onRetry: () =>
            ref.read(repliesControllerProvider(keyArg).notifier).retryMore(),
      );
    }
    return const SizedBox(height: 8);
  }
}

class _Message extends StatelessWidget {
  const _Message({required this.text, required this.onRetry});
  final String text;
  final VoidCallback onRetry;

  @override
  Widget build(BuildContext context) => Center(
    child: Column(
      mainAxisSize: MainAxisSize.min,
      children: [
        Text(text, style: VelaText.sans14.copyWith(color: VelaColors.muted)),
        TextButton(onPressed: onRetry, child: const Text('Try again')),
      ],
    ),
  );
}
