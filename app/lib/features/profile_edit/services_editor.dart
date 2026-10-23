import 'package:flutter/material.dart';
import 'package:vela_api_client/vela_api_client.dart';

import '../../core/theme/form_styles.dart';
import 'list_editor.dart';
import 'profile_form.dart';

class ServicesEditor extends StatelessWidget {
  const ServicesEditor({
    super.key,
    required this.items,
    required this.onChanged,
    this.enabled = true,
  });

  final List<ServiceItem> items;
  final ValueChanged<List<ServiceItem>> onChanged;
  final bool enabled;

  @override
  Widget build(BuildContext context) => ListEditor<ServiceItem>(
    idPrefix: 'service',
    label: 'Services',
    noun: 'service',
    items: items,
    max: maxServices,
    enabled: enabled,
    titleOf: (s) => s.title,
    subtitleOf: (s) =>
        [s.price, s.description].where((t) => t.isNotEmpty).join(' - '),
    open: _open,
    onChanged: onChanged,
  );

  Future<ServiceItem?> _open(BuildContext context, ServiceItem? existing) {
    final title = TextEditingController(text: existing?.title);
    final description = TextEditingController(text: existing?.description);
    final price = TextEditingController(text: existing?.price);
    return showItemSheet<ServiceItem>(
      context,
      idPrefix: 'service-form',
      title: existing == null ? 'Add a service' : 'Edit service',
      fields: [
        _field('service-title', 'Title', title),
        const SizedBox(height: 10),
        _field('service-description', 'Description', description, lines: 3),
        const SizedBox(height: 10),
        _field('service-price', 'Price, for example "From \$400"', price),
      ],
      problem: () => serviceProblem(title.text, description.text, price.text),
      build: () => ServiceItem(
        title: title.text.trim(),
        description: description.text.trim(),
        price: price.text.trim(),
      ),
    );
  }

  Widget _field(
    String key,
    String label,
    TextEditingController c, {
    int lines = 1,
  }) => Semantics(
    label: label,
    textField: true,
    child: TextField(
      key: ValueKey(key),
      controller: c,
      minLines: lines,
      maxLines: lines,
      textInputAction: lines == 1
          ? TextInputAction.next
          : TextInputAction.newline,
      decoration: velaInputDecoration(hint: label),
    ),
  );
}
