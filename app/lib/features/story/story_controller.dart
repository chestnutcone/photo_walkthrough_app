import 'dart:async';

import 'package:flutter/services.dart';
import 'package:flutter/widgets.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import 'story_settings.dart';

/// How long autoplay rests on each chapter.
const Duration autoplayInterval = Duration(seconds: 6);

enum StoryMode { story, explore }

/// Identifies one viewer session: the work and how many chapters it has.
@immutable
class StoryKey {
  const StoryKey({required this.workId, required this.chapterCount});

  final String workId;
  final int chapterCount;

  @override
  bool operator ==(Object other) =>
      other is StoryKey &&
      other.workId == workId &&
      other.chapterCount == chapterCount;

  @override
  int get hashCode => Object.hash(workId, chapterCount);
}

@immutable
class StoryState {
  const StoryState({
    this.step = 0,
    this.mode = StoryMode.story,
    this.exploreSel = -1,
    this.threadOpen = false,
    this.autoplay = false,
  });

  /// 0 = intro, 1..n = chapters, n + 1 = end card (Story mode).
  final int step;
  final StoryMode mode;

  /// Selected chapter index in Explore mode, -1 when none.
  final int exploreSel;
  final bool threadOpen;
  final bool autoplay;

  StoryState copyWith({
    int? step,
    StoryMode? mode,
    int? exploreSel,
    bool? threadOpen,
    bool? autoplay,
  }) => StoryState(
    step: step ?? this.step,
    mode: mode ?? this.mode,
    exploreSel: exploreSel ?? this.exploreSel,
    threadOpen: threadOpen ?? this.threadOpen,
    autoplay: autoplay ?? this.autoplay,
  );

  /// Index of the chapter on screen, or -1 (intro, end, explore idle).
  int chapterIndex(int chapterCount) {
    if (mode == StoryMode.explore) return exploreSel;
    return step >= 1 && step <= chapterCount ? step - 1 : -1;
  }

  @override
  bool operator ==(Object other) =>
      other is StoryState &&
      other.step == step &&
      other.mode == mode &&
      other.exploreSel == exploreSel &&
      other.threadOpen == threadOpen &&
      other.autoplay == autoplay;

  @override
  int get hashCode => Object.hash(step, mode, exploreSel, threadOpen, autoplay);
}

final storyControllerProvider = NotifierProvider.autoDispose
    .family<StoryController, StoryState, StoryKey>(StoryController.new);

class StoryController extends Notifier<StoryState> with WidgetsBindingObserver {
  StoryController(this._key);

  final StoryKey _key;
  Timer? _timer;
  bool _backgrounded = false;

  int get _n => _key.chapterCount;

  @override
  StoryState build() {
    WidgetsBinding.instance.addObserver(this);
    ref.onDispose(() {
      WidgetsBinding.instance.removeObserver(this);
      _timer?.cancel();
      _timer = null;
    });
    return StoryState(autoplay: ref.read(storyAutoplayDefaultProvider));
  }

  void next() {
    if (state.mode != StoryMode.story) return;
    _update(state.copyWith(step: _clampStep(state.step + 1)));
  }

  void prev() {
    if (state.mode != StoryMode.story) return;
    _update(state.copyWith(step: _clampStep(state.step - 1)));
  }

  void jump(int step) => _update(state.copyWith(step: _clampStep(step)));

  /// Switching mode always returns to the intro, unzoomed.
  void setMode(StoryMode mode) =>
      _update(StoryState(mode: mode, autoplay: state.autoplay));

  /// Story mode: walk to that chapter. Explore: zoom to it.
  void selectPin(int index) {
    if (index < 0 || index >= _n) return;
    if (state.mode == StoryMode.story) {
      _update(state.copyWith(step: index + 1));
    } else {
      _update(state.copyWith(exploreSel: index));
    }
  }

  void zoomOut() => _update(state.copyWith(exploreSel: -1));

  void setThreadOpen(bool open) => _update(state.copyWith(threadOpen: open));

  void toggleAutoplay() => _update(state.copyWith(autoplay: !state.autoplay));

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    _backgrounded = state != AppLifecycleState.resumed;
    _reschedule();
  }

  int _clampStep(int step) => step.clamp(0, _n + 1);

  void _update(StoryState next) {
    if (next == state) return;
    final before = state.chapterIndex(_n);
    state = next;
    final after = next.chapterIndex(_n);
    if (after != -1 && after != before) {
      unawaited(HapticFeedback.selectionClick());
    }
    _reschedule();
  }

  /// One-shot timer per chapter: any state change restarts the 6 s clock, and
  /// pausing conditions simply leave it unscheduled.
  void _reschedule() {
    _timer?.cancel();
    _timer = null;
    final running =
        state.autoplay &&
        state.mode == StoryMode.story &&
        state.step >= 1 &&
        state.step <= _n &&
        !state.threadOpen &&
        !_backgrounded;
    if (running) _timer = Timer(autoplayInterval, next);
  }
}
