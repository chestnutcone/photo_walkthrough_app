import 'package:flutter/material.dart';

import '../../core/theme/form_styles.dart';
import '../../core/theme/tokens.dart';

/// Add / edit / remove list with a cap. [open] shows the item form and
/// returns the saved item (null on cancel); [existing] is null when adding.
class ListEditor<T> extends StatelessWidget {
  const ListEditor({
    super.key,
    required this.idPrefix,
    required this.label,
    required this.noun,
    required this.items,
    required this.max,
    required this.titleOf,
    required this.subtitleOf,
    required this.open,
    required this.onChanged,
    this.enabled = true,
  });

  /// Prefix for widget keys: `<id>-add`, `<id>-edit-0`, `<id>-remove-0`.
  final String idPrefix;
  final String label;

  /// Singular, lower case: "service".
  final String noun;
  final List<T> items;
  final int max;
  final String Function(T) titleOf;
  final String Function(T) subtitleOf;
  final Future<T?> Function(BuildContext context, T? existing) open;
  final ValueChanged<List<T>> onChanged;
  final bool enabled;

  Future<void> _add(BuildContext context) async {
    final item = await open(context, null);
    if (item != null) onChanged([...items, item]);
  }

  Future<void> _edit(BuildContext context, int i) async {
    final item = await open(context, items[i]);
    if (item != null) {
      onChanged([
        for (var j = 0; j < items.length; j++) j == i ? item : items[j],
      ]);
    }
  }

  void _remove(int i) => onChanged([
    for (var j = 0; j < items.length; j++)
      if (j != i) items[j],
  ]);

  @override
  Widget build(BuildContext context) {
    final full = items.length >= max;
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Semantics(
          header: true,
          child: Text(
            '$label (${items.length}/$max)',
            style: VelaText.sans13s.copyWith(color: VelaColors.secondary),
          ),
        ),
        const SizedBox(height: 8),
        for (var i = 0; i < items.length; i++) _row(context, i),
        const SizedBox(height: 4),
        OutlinedButton.icon(
          key: ValueKey('$idPrefix-add'),
          style: velaOutlinedButtonStyle(),
          onPressed: enabled && !full ? () => _add(context) : null,
          icon: const Icon(Icons.add, size: 18),
          label: Text(full ? 'Limit reached' : 'Add $noun'),
        ),
      ],
    );
  }

  Widget _row(BuildContext context, int i) {
    final item = items[i];
    final subtitle = subtitleOf(item);
    return Container(
      margin: const EdgeInsets.only(bottom: 8),
      padding: const EdgeInsets.only(left: 14),
      decoration: BoxDecoration(
        color: VelaColors.input,
        borderRadius: BorderRadius.circular(10),
      ),
      child: Row(
        children: [
          Expanded(
            child: Padding(
              padding: const EdgeInsets.symmetric(vertical: 10),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    titleOf(item),
                    style: VelaText.sans14m.copyWith(color: VelaColors.ink),
                  ),
                  if (subtitle.isNotEmpty)
                    Text(
                      subtitle,
                      maxLines: 2,
                      overflow: TextOverflow.ellipsis,
                      style: VelaText.sans12.copyWith(color: VelaColors.muted),
                    ),
                ],
              ),
            ),
          ),
          IconButton(
            key: ValueKey('$idPrefix-edit-$i'),
            tooltip: 'Edit $noun ${titleOf(item)}',
            constraints: const BoxConstraints(minWidth: 48, minHeight: 48),
            onPressed: enabled ? () => _edit(context, i) : null,
            icon: const Icon(
              Icons.edit_outlined,
              size: 20,
              color: VelaColors.secondary,
            ),
          ),
          IconButton(
            key: ValueKey('$idPrefix-remove-$i'),
            tooltip: 'Remove $noun ${titleOf(item)}',
            constraints: const BoxConstraints(minWidth: 48, minHeight: 48),
            onPressed: enabled ? () => _remove(i) : null,
            icon: const Icon(
              Icons.close,
              size: 20,
              color: VelaColors.secondary,
            ),
          ),
        ],
      ),
    );
  }
}

/// Bottom sheet frame shared by the item forms: keyboard-safe, scrolls,
/// with an inline problem line and Save / Cancel.
Future<T?> showItemSheet<T>(
  BuildContext context, {
  required String title,
  required List<Widget> fields,
  required T? Function() build,
  required String? Function() problem,
  required String idPrefix,
}) => showModalBottomSheet<T>(
  context: context,
  isScrollControlled: true,
  useSafeArea: true,
  backgroundColor: VelaColors.sheet,
  builder: (_) => _ItemSheet<T>(
    title: title,
    fields: fields,
    build: build,
    problem: problem,
    idPrefix: idPrefix,
  ),
);

class _ItemSheet<T> extends StatefulWidget {
  const _ItemSheet({
    required this.title,
    required this.fields,
    required this.build,
    required this.problem,
    required this.idPrefix,
  });

  final String title;
  final List<Widget> fields;
  final T? Function() build;
  final String? Function() problem;
  final String idPrefix;

  @override
  State<_ItemSheet<T>> createState() => _ItemSheetState<T>();
}

class _ItemSheetState<T> extends State<_ItemSheet<T>> {
  String? _problem;

  void _save() {
    final p = widget.problem();
    if (p != null) {
      setState(() => _problem = p);
      return;
    }
    Navigator.of(context).pop(widget.build());
  }

  @override
  Widget build(BuildContext context) => Padding(
    padding: EdgeInsets.only(bottom: MediaQuery.viewInsetsOf(context).bottom),
    child: SingleChildScrollView(
      padding: const EdgeInsets.fromLTRB(20, 20, 20, 20),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Semantics(
            header: true,
            child: Text(widget.title, style: VelaText.serif24),
          ),
          const SizedBox(height: 14),
          ...widget.fields,
          if (_problem != null)
            Padding(
              padding: const EdgeInsets.only(top: 8),
              child: Semantics(
                liveRegion: true,
                child: Text(
                  _problem!,
                  key: ValueKey('${widget.idPrefix}-problem'),
                  style: VelaText.sans12.copyWith(color: VelaColors.danger),
                ),
              ),
            ),
          const SizedBox(height: 14),
          Row(
            children: [
              Expanded(
                child: OutlinedButton(
                  style: velaOutlinedButtonStyle(),
                  onPressed: () => Navigator.of(context).pop(),
                  child: const Text('Cancel'),
                ),
              ),
              const SizedBox(width: 10),
              Expanded(
                child: FilledButton(
                  key: ValueKey('${widget.idPrefix}-save'),
                  style: velaFilledButtonStyle(),
                  onPressed: _save,
                  child: const Text('Done'),
                ),
              ),
            ],
          ),
        ],
      ),
    ),
  );
}
