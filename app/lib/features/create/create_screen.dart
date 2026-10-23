import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/theme/form_styles.dart';
import '../../core/theme/tokens.dart';
import '../story/motion.dart';
import 'chapter_form.dart';
import 'create_controller.dart';
import 'create_messages.dart';
import 'create_state.dart';
import 'photo_picker.dart';
import 'pin_draft.dart';
import 'pin_editor.dart';

/// Create a story: pick a photograph, place and write chapters, publish.
/// The route requires sign-in (`requireSignIn` runs at the call sites).
class CreateScreen extends ConsumerStatefulWidget {
  const CreateScreen({super.key});

  @override
  ConsumerState<CreateScreen> createState() => _CreateScreenState();
}

class _CreateScreenState extends ConsumerState<CreateScreen> {
  final _formKey = GlobalKey();
  bool _leaving = false;

  CreateController get _ctl => ref.read(createControllerProvider.notifier);

  void _exit() {
    if (_leaving || !mounted) return;
    _leaving = true;
    if (context.canPop()) {
      context.pop();
    } else {
      context.go('/');
    }
  }

  Future<void> _requestLeave() async {
    final s = ref.read(createControllerProvider);
    if (s.phase == CreatePhase.annotate && s.hasUnsavedWork) {
      final discard = await showDialog<bool>(
        context: context,
        builder: (_) => const _DiscardDialog(),
      );
      if (discard != true || !mounted) return;
    }
    if (s.phase == CreatePhase.publishing) {
      _ctl.stop();
    } else {
      await _ctl.discardDraft();
    }
    _exit();
  }

  void _scrollToForm() {
    WidgetsBinding.instance.addPostFrameCallback((_) {
      final ctx = _formKey.currentContext;
      if (ctx == null || !ctx.mounted) return;
      Scrollable.ensureVisible(
        ctx,
        duration: motionDuration(ctx, const Duration(milliseconds: 250)),
        alignmentPolicy: ScrollPositionAlignmentPolicy.keepVisibleAtEnd,
      );
    });
  }

  @override
  Widget build(BuildContext context) {
    ref.listen(createControllerProvider, (prev, next) {
      if (next.aborted && !(prev?.aborted ?? false)) {
        _leaving = true;
        context.go('/');
        return;
      }
      if (next.outcome == PublishOutcome.published &&
          prev?.outcome != PublishOutcome.published &&
          next.workId != null) {
        _leaving = true;
        context.go('/story/${next.workId}');
      }
      if (prev?.ghost == null && next.ghost != null) _scrollToForm();
    });
    final s = ref.watch(createControllerProvider);
    return PopScope(
      canPop: false,
      onPopInvokedWithResult: (didPop, _) {
        if (!didPop) _requestLeave();
      },
      child: Scaffold(
        key: const ValueKey('create-screen'),
        backgroundColor: VelaColors.background,
        body: SafeArea(
          child: Column(
            children: [
              _Header(
                state: s,
                onCancel: _requestLeave,
                onPublish: () => _ctl.publish(),
              ),
              if (s.error != null && s.phase != CreatePhase.pick)
                _ErrorBanner(text: s.error!),
              Expanded(
                child: switch (s.phase) {
                  CreatePhase.pick => _PickView(state: s),
                  CreatePhase.annotate => _AnnotateView(
                    state: s,
                    formKey: _formKey,
                  ),
                  CreatePhase.publishing => _PublishingView(
                    state: s,
                    onDone: _exit,
                  ),
                },
              ),
            ],
          ),
        ),
      ),
    );
  }
}

class _DiscardDialog extends StatelessWidget {
  const _DiscardDialog();

  @override
  Widget build(BuildContext context) => AlertDialog(
    backgroundColor: VelaColors.sheet,
    title: Text('Discard this story?', style: VelaText.serif24),
    content: Text(
      'Your chapters have not been published and will be lost.',
      style: VelaText.sans14,
    ),
    actions: [
      TextButton(
        key: const ValueKey('keep-editing'),
        style: velaTextButtonStyle(),
        onPressed: () => Navigator.of(context).pop(false),
        child: const Text('Keep editing'),
      ),
      TextButton(
        key: const ValueKey('confirm-discard'),
        style: velaTextButtonStyle().copyWith(
          foregroundColor: const WidgetStatePropertyAll(VelaColors.danger),
        ),
        onPressed: () => Navigator.of(context).pop(true),
        child: const Text('Discard'),
      ),
    ],
  );
}

class _Header extends StatelessWidget {
  const _Header({
    required this.state,
    required this.onCancel,
    required this.onPublish,
  });

  final CreateState state;
  final VoidCallback onCancel;
  final VoidCallback onPublish;

  @override
  Widget build(BuildContext context) {
    final n = state.pins.length;
    final label = n == 0
        ? 'NEW STORY'
        : '$n ${n == 1 ? 'CHAPTER' : 'CHAPTERS'}';
    final showPublish = state.phase == CreatePhase.annotate;
    return Padding(
      padding: const EdgeInsets.fromLTRB(8, 4, 16, 4),
      child: Row(
        children: [
          TextButton(
            key: const ValueKey('cancel'),
            style: velaTextButtonStyle(),
            onPressed: onCancel,
            child: const Text('Cancel'),
          ),
          Expanded(
            child: Text(
              label,
              textAlign: TextAlign.center,
              style: VelaText.mono11,
            ),
          ),
          SizedBox(
            width: 88,
            child: showPublish
                ? _PublishPill(enabled: state.canPublish, onTap: onPublish)
                : null,
          ),
        ],
      ),
    );
  }
}

class _PublishPill extends StatelessWidget {
  const _PublishPill({required this.enabled, required this.onTap});

  final bool enabled;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) => Semantics(
    button: true,
    enabled: enabled,
    label: 'Publish',
    excludeSemantics: true,
    child: Material(
      key: const ValueKey('publish'),
      color: enabled ? VelaColors.ink : VelaColors.input,
      shape: const StadiumBorder(),
      child: InkWell(
        customBorder: const StadiumBorder(),
        onTap: enabled ? onTap : null,
        child: ConstrainedBox(
          constraints: const BoxConstraints(minHeight: 44, minWidth: 44),
          child: Center(
            child: Text(
              'Publish',
              style: VelaText.sans13s.copyWith(
                color: enabled ? VelaColors.background : VelaColors.faint,
              ),
            ),
          ),
        ),
      ),
    ),
  );
}

class _ErrorBanner extends StatelessWidget {
  const _ErrorBanner({required this.text});
  final String text;

  @override
  Widget build(BuildContext context) => Semantics(
    liveRegion: true,
    child: Container(
      key: const ValueKey('create-error'),
      width: double.infinity,
      margin: const EdgeInsets.fromLTRB(16, 0, 16, 8),
      padding: const EdgeInsets.all(12),
      decoration: BoxDecoration(
        color: VelaColors.input,
        borderRadius: BorderRadius.circular(10),
      ),
      child: Text(
        text,
        style: VelaText.sans13.copyWith(color: VelaColors.danger),
      ),
    ),
  );
}

class _PickView extends ConsumerWidget {
  const _PickView({required this.state});
  final CreateState state;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final ctl = ref.read(createControllerProvider.notifier);
    return SingleChildScrollView(
      padding: const EdgeInsets.fromLTRB(24, 24, 24, 24),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Semantics(
            header: true,
            child: Text('Choose a photograph', style: VelaText.serif36),
          ),
          const SizedBox(height: 10),
          Text(
            'Pick the image you want to walk people through. JPEG, PNG or '
            'WebP.',
            style: VelaText.sans15.copyWith(color: VelaColors.muted),
          ),
          const SizedBox(height: 28),
          if (state.error != null) ...[
            Semantics(
              liveRegion: true,
              child: Text(
                state.error!,
                key: const ValueKey('create-error'),
                style: VelaText.sans14.copyWith(color: VelaColors.danger),
              ),
            ),
            const SizedBox(height: 16),
          ],
          if (state.busy)
            const Center(
              child: CircularProgressIndicator(color: VelaColors.accent),
            )
          else ...[
            FilledButton(
              key: const ValueKey('pick-library'),
              style: velaFilledButtonStyle(),
              onPressed: () => ctl.pickPhoto(PhotoSource.library),
              child: const Text('Choose from library'),
            ),
            const SizedBox(height: 12),
            OutlinedButton(
              key: const ValueKey('pick-camera'),
              style: velaOutlinedButtonStyle(),
              onPressed: () => ctl.pickPhoto(PhotoSource.camera),
              child: const Text('Take a photo'),
            ),
          ],
        ],
      ),
    );
  }
}

class _AnnotateView extends ConsumerWidget {
  const _AnnotateView({required this.state, required this.formKey});

  final CreateState state;
  final GlobalKey formKey;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final ctl = ref.read(createControllerProvider.notifier);
    final ghost = state.ghost;
    return ListView(
      keyboardDismissBehavior: ScrollViewKeyboardDismissBehavior.onDrag,
      padding: const EdgeInsets.only(bottom: 48),
      children: [
        PinEditor(
          photo: state.photo!,
          pins: state.pins,
          ghost: ghost,
          editingIndex: state.editingIndex,
          onTapPhoto: ctl.placeGhost,
          onTapPin: ctl.editPin,
        ),
        if (state.busy)
          const Padding(
            padding: EdgeInsets.symmetric(vertical: 8),
            child: LinearProgressIndicator(
              color: VelaColors.accent,
              backgroundColor: VelaColors.input,
            ),
          ),
        if (state.uploadFailed)
          Padding(
            padding: const EdgeInsets.symmetric(horizontal: 16),
            child: OutlinedButton(
              key: const ValueKey('retry-upload'),
              style: velaOutlinedButtonStyle(),
              onPressed: state.busy ? null : ctl.retryUpload,
              child: const Text('Retry upload'),
            ),
          ),
        Padding(
          padding: const EdgeInsets.fromLTRB(16, 16, 16, 0),
          child: _StoryFields(state: state),
        ),
        Padding(
          key: formKey,
          padding: const EdgeInsets.fromLTRB(16, 20, 16, 0),
          child: ghost == null
              ? (state.pins.isEmpty ? const _Hint() : const SizedBox.shrink())
              : ChapterForm(
                  key: ValueKey('form-${state.editingIndex}'),
                  number: (state.editingIndex ?? state.pins.length) + 1,
                  initial: ghost,
                  editing: state.editingIndex != null,
                  onSubmit:
                      ({
                        required title,
                        required body,
                        required zoomIn,
                        meta,
                        altText,
                      }) => ctl.commitGhost(
                        title: title,
                        body: body,
                        zoomIn: zoomIn,
                        meta: meta,
                        altText: altText,
                      ),
                  onDiscard: ctl.discardGhost,
                ),
        ),
        for (var i = 0; i < state.pins.length; i++)
          _ChapterRow(
            index: i,
            pin: state.pins[i],
            onEdit: () => ctl.editPin(i),
            onRemove: () => ctl.removePin(i),
          ),
      ],
    );
  }
}

class _Hint extends StatelessWidget {
  const _Hint();

  @override
  Widget build(BuildContext context) => Text(
    'Tap anything in the photograph to start a chapter. Chapters play in the '
    'order you add them.',
    style: VelaText.sans14.copyWith(color: VelaColors.muted),
  );
}

class _StoryFields extends ConsumerStatefulWidget {
  const _StoryFields({required this.state});
  final CreateState state;

  @override
  ConsumerState<_StoryFields> createState() => _StoryFieldsState();
}

class _StoryFieldsState extends ConsumerState<_StoryFields> {
  late final _title = TextEditingController(text: widget.state.title);
  late final _alt = TextEditingController(text: widget.state.altText);

  @override
  void dispose() {
    _title.dispose();
    _alt.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final ctl = ref.read(createControllerProvider.notifier);
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        TextField(
          key: const ValueKey('story-title'),
          controller: _title,
          maxLength: maxStoryTitle,
          style: VelaText.serif28,
          textCapitalization: TextCapitalization.sentences,
          buildCounter: (
            _, {
            required currentLength,
            required isFocused,
            maxLength,
          }) => null,
          onChanged: ctl.setTitle,
          decoration: const InputDecoration(
            hintText: defaultStoryTitle,
            border: UnderlineInputBorder(
              borderSide: BorderSide(color: VelaColors.outline),
            ),
            enabledBorder: UnderlineInputBorder(
              borderSide: BorderSide(color: VelaColors.outline),
            ),
          ),
        ),
        const SizedBox(height: 12),
        TextField(
          key: const ValueKey('story-alt'),
          controller: _alt,
          maxLength: maxStoryAlt,
          style: VelaText.sans14,
          buildCounter: (
            _, {
            required currentLength,
            required isFocused,
            maxLength,
          }) => null,
          onChanged: ctl.setAltText,
          decoration: velaInputDecoration(
            hint: 'Describe the photograph for screen readers (optional)',
          ),
        ),
      ],
    );
  }
}

class _ChapterRow extends StatelessWidget {
  const _ChapterRow({
    required this.index,
    required this.pin,
    required this.onEdit,
    required this.onRemove,
  });

  final int index;
  final PinDraft pin;
  final VoidCallback onEdit;
  final VoidCallback onRemove;

  @override
  Widget build(BuildContext context) => Padding(
    padding: const EdgeInsets.fromLTRB(16, 16, 4, 0),
    child: Row(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Expanded(
          child: Semantics(
            button: true,
            label: 'Edit chapter ${index + 1}: ${pin.title}',
            excludeSemantics: true,
            child: InkWell(
              key: ValueKey('chapter-row-$index'),
              onTap: onEdit,
              child: ConstrainedBox(
                constraints: const BoxConstraints(minHeight: 44),
                child: Row(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    SizedBox(
                      width: 28,
                      child: Text(
                        '${index + 1}',
                        style: VelaText.mono12m.copyWith(
                          color: VelaColors.accent,
                        ),
                      ),
                    ),
                    Expanded(
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Text(pin.title, style: VelaText.serif18),
                          if (pin.body.isNotEmpty)
                            Text(
                              pin.body,
                              maxLines: 2,
                              overflow: TextOverflow.ellipsis,
                              style: VelaText.sans13.copyWith(
                                color: VelaColors.muted,
                              ),
                            ),
                          Text(
                            pin.zoom == null ? 'STAY PUT' : 'ZOOM IN',
                            style: VelaText.mono10,
                          ),
                        ],
                      ),
                    ),
                  ],
                ),
              ),
            ),
          ),
        ),
        IconButton(
          key: ValueKey('remove-$index'),
          tooltip: 'Remove chapter ${index + 1}',
          constraints: const BoxConstraints(minWidth: 44, minHeight: 44),
          icon: const Icon(Icons.close, color: VelaColors.faint, size: 20),
          onPressed: onRemove,
        ),
      ],
    ),
  );
}

class _PublishingView extends StatelessWidget {
  const _PublishingView({required this.state, required this.onDone});

  final CreateState state;
  final VoidCallback onDone;

  @override
  Widget build(BuildContext context) {
    final outcome = state.outcome ?? PublishOutcome.processing;
    final message = switch (outcome) {
      PublishOutcome.blocked => blockedMessage,
      PublishOutcome.removed => removedMessage,
      PublishOutcome.stillProcessing => stillProcessingMessage,
      _ => 'Publishing your story',
    };
    final working = outcome == PublishOutcome.processing;
    return Center(
      child: Padding(
        padding: const EdgeInsets.all(32),
        child: Semantics(
          liveRegion: true,
          child: Column(
            key: const ValueKey('publish-status'),
            mainAxisSize: MainAxisSize.min,
            children: [
              if (working)
                const CircularProgressIndicator(color: VelaColors.accent),
              if (working) const SizedBox(height: 20),
              Text(
                message,
                textAlign: TextAlign.center,
                style: VelaText.serif24,
              ),
              if (!working) ...[
                const SizedBox(height: 24),
                FilledButton(
                  key: const ValueKey('publish-done'),
                  style: velaFilledButtonStyle(),
                  onPressed: onDone,
                  child: const Text('Done'),
                ),
              ],
            ],
          ),
        ),
      ),
    );
  }
}
