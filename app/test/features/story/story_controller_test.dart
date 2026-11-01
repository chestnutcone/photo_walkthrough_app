import 'package:fake_async/fake_async.dart';
import 'package:flutter/services.dart';
import 'package:flutter/widgets.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:vela_app/features/story/story_controller.dart';
import 'package:vela_app/features/story/story_settings.dart';

const _key = StoryKey(workId: 'w1', chapterCount: 3);

ProviderContainer _container({bool autoplay = false}) {
  final c = ProviderContainer(
    overrides: [storyAutoplayDefaultProvider.overrideWithValue(autoplay)],
  );
  addTearDown(c.dispose);
  c.listen(storyControllerProvider(_key), (_, _) {});
  return c;
}

StoryController _ctl(ProviderContainer c) =>
    c.read(storyControllerProvider(_key).notifier);

StoryState _state(ProviderContainer c) => c.read(storyControllerProvider(_key));

void main() {
  TestWidgetsFlutterBinding.ensureInitialized();

  test('starts_on_intro_in_story_mode', () {
    final c = _container();
    expect(_state(c).step, 0);
    expect(_state(c).mode, StoryMode.story);
    expect(_state(c).exploreSel, -1);
    expect(_state(c).threadOpen, isFalse);
  });

  test('next_from_intro_goes_to_chapter_1', () {
    final c = _container();
    _ctl(c).next();
    expect(_state(c).step, 1);
  });

  test('prev_at_chapter_1_goes_to_intro', () {
    final c = _container();
    _ctl(c).next();
    _ctl(c).prev();
    expect(_state(c).step, 0);
  });

  test('prev_at_intro_stays_on_intro', () {
    final c = _container();
    _ctl(c).prev();
    expect(_state(c).step, 0);
  });

  test('next_past_last_goes_to_end', () {
    final c = _container();
    for (var i = 0; i < 4; i++) {
      _ctl(c).next();
    }
    expect(_state(c).step, 4);
    _ctl(c).next();
    expect(_state(c).step, 4, reason: 'end card is terminal');
  });

  test('jump_clamps_into_valid_steps', () {
    final c = _container();
    _ctl(c).jump(2);
    expect(_state(c).step, 2);
    _ctl(c).jump(99);
    expect(_state(c).step, 4);
    _ctl(c).jump(-5);
    expect(_state(c).step, 0);
  });

  test('mode_toggle_resets_to_intro_and_unzoomed', () {
    final c = _container();
    _ctl(c).setMode(StoryMode.explore);
    _ctl(c).selectPin(1);
    expect(_state(c).exploreSel, 1);
    _ctl(c).setMode(StoryMode.story);
    _ctl(c).jump(2);
    _ctl(c).setMode(StoryMode.explore);
    expect(_state(c).step, 0);
    expect(_state(c).exploreSel, -1);
    expect(_state(c).mode, StoryMode.explore);
  });

  test('explore_pin_tap_selects_and_zoom_out_clears', () {
    final c = _container();
    _ctl(c).setMode(StoryMode.explore);
    _ctl(c).selectPin(2);
    expect(_state(c).exploreSel, 2);
    _ctl(c).zoomOut();
    expect(_state(c).exploreSel, -1);
  });

  test('story_mode_pin_tap_jumps_to_that_chapter', () {
    final c = _container();
    _ctl(c).selectPin(1);
    expect(_state(c).step, 2);
  });

  test('select_pin_out_of_range_is_ignored', () {
    final c = _container();
    _ctl(c).setMode(StoryMode.explore);
    _ctl(c).selectPin(7);
    expect(_state(c).exploreSel, -1);
  });

  test('copy_with_keeps_untouched_fields', () {
    const s = StoryState(step: 2, exploreSel: 1, threadOpen: true);
    final t = s.copyWith(step: 3);
    expect(t.step, 3);
    expect(t.exploreSel, 1);
    expect(t.threadOpen, isTrue);
    expect(s.step, 2, reason: 'original untouched');
  });

  test('chapter_change_triggers_selection_haptic', () {
    final calls = <String>[];
    TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger
        .setMockMethodCallHandler(SystemChannels.platform, (call) async {
          if (call.method == 'HapticFeedback.vibrate') {
            calls.add(call.arguments as String);
          }
          return null;
        });
    addTearDown(
      () => TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger
          .setMockMethodCallHandler(SystemChannels.platform, null),
    );
    final c = _container();
    _ctl(c).next();
    expect(calls, ['HapticFeedbackType.selectionClick']);
  });

  group('autoplay', () {
    test('advances_every_6s_and_stops_at_end', () {
      fakeAsync((async) {
        final c = _container(autoplay: true);
        _ctl(c).next();
        async.elapse(const Duration(seconds: 5));
        expect(_state(c).step, 1);
        async.elapse(const Duration(seconds: 1));
        expect(_state(c).step, 2);
        async.elapse(const Duration(seconds: 12));
        expect(_state(c).step, 4);
        async.elapse(const Duration(seconds: 30));
        expect(_state(c).step, 4);
      });
    });

    test('does_not_run_from_intro_or_in_explore', () {
      fakeAsync((async) {
        final c = _container(autoplay: true);
        async.elapse(const Duration(seconds: 20));
        expect(_state(c).step, 0);
        _ctl(c).setMode(StoryMode.explore);
        async.elapse(const Duration(seconds: 20));
        expect(_state(c).step, 0);
      });
    });

    test('pauses_while_thread_open_and_resumes_after', () {
      fakeAsync((async) {
        final c = _container(autoplay: true);
        _ctl(c).next();
        _ctl(c).setThreadOpen(true);
        async.elapse(const Duration(seconds: 30));
        expect(_state(c).step, 1);
        _ctl(c).setThreadOpen(false);
        async.elapse(const Duration(seconds: 6));
        expect(_state(c).step, 2);
      });
    });

    test('pauses_while_app_backgrounded', () {
      fakeAsync((async) {
        final c = _container(autoplay: true);
        _ctl(c).next();
        _ctl(c).didChangeAppLifecycleState(AppLifecycleState.paused);
        async.elapse(const Duration(seconds: 30));
        expect(_state(c).step, 1);
        _ctl(c).didChangeAppLifecycleState(AppLifecycleState.resumed);
        async.elapse(const Duration(seconds: 6));
        expect(_state(c).step, 2);
      });
    });

    test('toggle_off_cancels_pending_tick', () {
      fakeAsync((async) {
        final c = _container(autoplay: true);
        _ctl(c).next();
        _ctl(c).toggleAutoplay();
        expect(_state(c).autoplay, isFalse);
        async.elapse(const Duration(seconds: 30));
        expect(_state(c).step, 1);
        _ctl(c).toggleAutoplay();
        async.elapse(const Duration(seconds: 6));
        expect(_state(c).step, 2);
      });
    });

    test('dispose_cancels_timer', () {
      fakeAsync((async) {
        final c = ProviderContainer(
          overrides: [storyAutoplayDefaultProvider.overrideWithValue(true)],
        );
        final sub = c.listen(storyControllerProvider(_key), (_, _) {});
        c.read(storyControllerProvider(_key).notifier).next();
        sub.close();
        c.dispose();
        expect(async.pendingTimers, isEmpty);
      });
    });
  });
}
