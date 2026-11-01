import 'package:flutter/material.dart';

import '../../core/theme/form_styles.dart';
import '../../core/theme/tokens.dart';

Future<bool> _confirm(
  BuildContext context, {
  required String title,
  required String body,
  required String yes,
  required String no,
  required String idPrefix,
}) async {
  final result = await showDialog<bool>(
    context: context,
    builder: (ctx) => AlertDialog(
      backgroundColor: VelaColors.sheet,
      title: Text(title, style: VelaText.serif24),
      content: Text(
        body,
        style: VelaText.sans14.copyWith(color: VelaColors.secondary),
      ),
      actions: [
        TextButton(
          key: ValueKey('$idPrefix-no'),
          style: velaTextButtonStyle(),
          onPressed: () => Navigator.of(ctx).pop(false),
          child: Text(no),
        ),
        FilledButton(
          key: ValueKey('$idPrefix-yes'),
          style: velaFilledButtonStyle(),
          onPressed: () => Navigator.of(ctx).pop(true),
          child: Text(yes),
        ),
      ],
    ),
  );
  return result ?? false;
}

Future<bool> confirmDiscard(BuildContext context) => _confirm(
  context,
  title: 'Discard changes?',
  body: 'Your edits have not been saved.',
  yes: 'Discard',
  no: 'Keep editing',
  idPrefix: 'discard',
);

Future<bool> confirmHandleChange(BuildContext context, String handle) =>
    _confirm(
      context,
      title: 'Change your handle?',
      body:
          'Your profile will move to @$handle. Links to your old handle will '
          'stop working, and you can change it again only after 30 days.',
      yes: 'Change handle',
      no: 'Cancel',
      idPrefix: 'handle-confirm',
    );
