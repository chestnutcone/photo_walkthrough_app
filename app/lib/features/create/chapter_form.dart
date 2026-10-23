import 'package:flutter/material.dart';

import '../../core/theme/form_styles.dart';
import '../../core/theme/tokens.dart';
import 'utf16_limit.dart';
import 'pin_draft.dart';

typedef ChapterSubmit = void Function({
  required String title,
  required String body,
  required bool zoomIn,
  String? meta,
  String? altText,
});

/// The form under the photo for the chapter being placed or edited.
class ChapterForm extends StatefulWidget {
  const ChapterForm({
    super.key,
    required this.number,
    required this.initial,
    required this.editing,
    required this.onSubmit,
    required this.onDiscard,
  });

  final int number;
  final PinDraft initial;
  final bool editing;
  final ChapterSubmit onSubmit;
  final VoidCallback onDiscard;

  @override
  State<ChapterForm> createState() => _ChapterFormState();
}

class _ChapterFormState extends State<ChapterForm> {
  late final _title = TextEditingController(text: widget.initial.title);
  late final _body = TextEditingController(text: widget.initial.body);
  late final _meta = TextEditingController(text: widget.initial.meta ?? '');
  late final _alt = TextEditingController(text: widget.initial.altText ?? '');
  late bool _zoomIn = widget.initial.zoom != null;

  @override
  void initState() {
    super.initState();
    _title.addListener(() => setState(() {}));
  }

  @override
  void dispose() {
    _title.dispose();
    _body.dispose();
    _meta.dispose();
    _alt.dispose();
    super.dispose();
  }

  void _submit() => widget.onSubmit(
    title: _title.text,
    body: _body.text,
    zoomIn: _zoomIn,
    meta: _meta.text,
    altText: _alt.text,
  );

  @override
  Widget build(BuildContext context) {
    final hasTitle = _title.text.trim().isNotEmpty;
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Text(
          'CHAPTER ${widget.number.toString().padLeft(2, '0')}',
          style: VelaText.monoKicker,
        ),
        const SizedBox(height: 10),
        _Field(
          fieldKey: const ValueKey('chapter-title'),
          controller: _title,
          hint: 'What is this detail?',
          maxLength: maxPinTitle,
          autofocus: true,
        ),
        const SizedBox(height: 10),
        _Field(
          fieldKey: const ValueKey('chapter-body'),
          controller: _body,
          hint: 'Tell the story of it',
          maxLength: maxPinBody,
          lines: 4,
        ),
        const SizedBox(height: 10),
        _Field(
          fieldKey: const ValueKey('chapter-meta'),
          controller: _meta,
          hint: 'Optional note, such as lens or place',
          maxLength: maxPinMeta,
        ),
        const SizedBox(height: 10),
        _Field(
          fieldKey: const ValueKey('chapter-alt'),
          controller: _alt,
          hint: 'Describe this detail for screen readers (optional)',
          maxLength: maxPinAlt,
        ),
        const SizedBox(height: 14),
        Row(
          children: [
            _ZoomChoice(
              chipKey: const ValueKey('zoom-in'),
              label: 'Zoom in',
              selected: _zoomIn,
              onTap: () => setState(() => _zoomIn = true),
            ),
            const SizedBox(width: 8),
            _ZoomChoice(
              chipKey: const ValueKey('stay-put'),
              label: 'Stay put',
              selected: !_zoomIn,
              onTap: () => setState(() => _zoomIn = false),
            ),
          ],
        ),
        const SizedBox(height: 16),
        Row(
          children: [
            TextButton(
              key: const ValueKey('discard-ghost'),
              style: velaTextButtonStyle(),
              onPressed: widget.onDiscard,
              child: Text(widget.editing ? 'Cancel' : 'Discard'),
            ),
            const Spacer(),
            FilledButton(
              key: const ValueKey('add-chapter'),
              style: velaFilledButtonStyle(),
              onPressed: hasTitle ? _submit : null,
              child: Text(widget.editing ? 'Save chapter' : 'Add chapter'),
            ),
          ],
        ),
      ],
    );
  }
}

class _Field extends StatelessWidget {
  const _Field({
    required this.fieldKey,
    required this.controller,
    required this.hint,
    required this.maxLength,
    this.lines = 1,
    this.autofocus = false,
  });

  final Key fieldKey;
  final TextEditingController controller;
  final String hint;
  final int maxLength;
  final int lines;
  final bool autofocus;

  @override
  Widget build(BuildContext context) => TextField(
    key: fieldKey,
    controller: controller,
    autofocus: autofocus,
    minLines: lines,
    maxLines: lines == 1 ? 1 : lines + 2,
    inputFormatters: [Utf16LengthFormatter(maxLength)],
    textCapitalization: TextCapitalization.sentences,
    style: VelaText.sans15,
    decoration: velaInputDecoration(hint: hint),
  );
}

class _ZoomChoice extends StatelessWidget {
  const _ZoomChoice({
    required this.chipKey,
    required this.label,
    required this.selected,
    required this.onTap,
  });

  final Key chipKey;
  final String label;
  final bool selected;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) => Semantics(
    button: true,
    selected: selected,
    label: label,
    excludeSemantics: true,
    child: Material(
      key: chipKey,
      color: selected ? VelaColors.ink : Colors.transparent,
      shape: StadiumBorder(
        side: BorderSide(color: selected ? VelaColors.ink : VelaColors.outline),
      ),
      child: InkWell(
        customBorder: const StadiumBorder(),
        onTap: onTap,
        child: ConstrainedBox(
          constraints: const BoxConstraints(minHeight: 44, minWidth: 44),
          child: Padding(
            padding: const EdgeInsets.symmetric(horizontal: 18),
            child: Center(
              widthFactor: 1,
              child: Text(
                label,
                style: VelaText.sans13m.copyWith(
                  color: selected ? VelaColors.background : VelaColors.ink,
                ),
              ),
            ),
          ),
        ),
      ),
    ),
  );
}
