import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:vela_api_client/vela_api_client.dart';

import '../../core/theme/form_styles.dart';
import 'list_editor.dart';
import 'profile_form.dart';

class ShowsEditor extends StatelessWidget {
  const ShowsEditor({
    super.key,
    required this.items,
    required this.onChanged,
    this.enabled = true,
  });

  final List<ShowItem> items;
  final ValueChanged<List<ShowItem>> onChanged;
  final bool enabled;

  @override
  Widget build(BuildContext context) => ListEditor<ShowItem>(
    idPrefix: 'show',
    label: 'Shows and exhibitions',
    noun: 'show',
    items: items,
    max: maxShows,
    enabled: enabled,
    titleOf: (s) => s.year,
    subtitleOf: (s) => s.text,
    open: _open,
    onChanged: onChanged,
  );

  Future<ShowItem?> _open(BuildContext context, ShowItem? existing) {
    final year = TextEditingController(text: existing?.year);
    final text = TextEditingController(text: existing?.text);
    return showItemSheet<ShowItem>(
      context,
      idPrefix: 'show-form',
      controllers: [year, text],
      title: existing == null ? 'Add a show' : 'Edit show',
      fields: [
        Semantics(
          label: 'Year',
          textField: true,
          child: TextField(
            key: const ValueKey('show-year'),
            controller: year,
            keyboardType: TextInputType.number,
            inputFormatters: [
              FilteringTextInputFormatter.digitsOnly,
              LengthLimitingTextInputFormatter(4),
            ],
            textInputAction: TextInputAction.next,
            decoration: velaInputDecoration(hint: 'Year, for example 2024'),
          ),
        ),
        const SizedBox(height: 10),
        Semantics(
          label: 'Show or exhibition',
          textField: true,
          child: TextField(
            key: const ValueKey('show-text'),
            controller: text,
            decoration: velaInputDecoration(hint: 'Show title and venue'),
          ),
        ),
      ],
      problem: () => showProblem(year.text, text.text),
      build: () => ShowItem(year: year.text.trim(), text: text.text.trim()),
    );
  }
}
