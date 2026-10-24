import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:vela_api_client/vela_api_client.dart';

import '../../core/api/api_exception.dart';
import '../../core/api/api_providers.dart';
import '../../core/theme/form_styles.dart';
import '../../core/theme/tokens.dart';
import '../create/utf16_limit.dart';
import '../onboarding/legal_links.dart';
import 'report_messages.dart';
import 'report_reasons.dart';

/// Opens the report sheet. True when a report was filed (also when the user
/// swiped the thank-you away), so the caller can hide the item locally.
/// Callers make sure the viewer is signed in first (see `reportSheetHook`).
Future<bool> showReportSheet(
  BuildContext context, {
  required String targetType,
  required String targetId,
}) async {
  var filed = false;
  await showModalBottomSheet<void>(
    context: context,
    isScrollControlled: true,
    useSafeArea: true,
    backgroundColor: VelaColors.sheet,
    barrierColor: const Color(0x66000000),
    shape: const RoundedRectangleBorder(
      borderRadius: BorderRadius.vertical(top: Radius.circular(26)),
    ),
    builder: (_) => ReportSheet(
      targetType: targetType,
      targetId: targetId,
      onFiled: () => filed = true,
    ),
  );
  return filed;
}

class ReportSheet extends ConsumerStatefulWidget {
  const ReportSheet({
    super.key,
    required this.targetType,
    required this.targetId,
    required this.onFiled,
  });

  final String targetType;
  final String targetId;
  final VoidCallback onFiled;

  @override
  ConsumerState<ReportSheet> createState() => _ReportSheetState();
}

class _ReportSheetState extends ConsumerState<ReportSheet> {
  final _note = TextEditingController();
  ReportReason? _reason;
  bool _sending = false;
  bool _done = false;
  ApiException? _error;

  @override
  void dispose() {
    _note.dispose();
    super.dispose();
  }

  Future<void> _send() async {
    final reason = _reason;
    if (reason == null || _sending) return; // double-submit guard
    final note = _note.text.trim();
    setState(() {
      _sending = true;
      _error = null;
    });
    try {
      final env = (await apiGuard(
        () => ref
            .read(apiProvider)
            .getSafetyApi()
            .report(
              reportRequest: ReportRequest(
                targetType: reportTargetType(widget.targetType),
                targetId: widget.targetId,
                reason: reason.api,
                note: note.isEmpty ? null : note,
              ),
            ),
      )).data;
      if (env?.success != true) {
        throw ApiException(
          code: env?.error?.code ?? 'unknown_error',
          message: '',
        );
      }
      widget.onFiled();
      if (mounted) setState(() => _done = true);
    } on ApiException catch (e) {
      if (mounted) setState(() => _error = e);
    } finally {
      if (mounted) setState(() => _sending = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final inset = MediaQuery.viewInsetsOf(context).bottom;
    // A send in flight must not be dismissed: the result would be lost.
    return PopScope(
      canPop: !_sending,
      child: Padding(
        padding: EdgeInsets.only(bottom: inset),
        child: SingleChildScrollView(
          padding: const EdgeInsets.fromLTRB(20, 14, 20, 24),
          child: _done ? _thanks(context) : _form(context),
        ),
      ),
    );
  }

  Widget _thanks(BuildContext context) => Column(
    crossAxisAlignment: CrossAxisAlignment.start,
    children: [
      const SizedBox(height: 10),
      Text('Thanks for letting us know', style: VelaText.serif26),
      const SizedBox(height: 10),
      Text(
        widget.targetType == 'user'
            ? "We'll review it."
            : "We'll review it. Until then it's hidden for you.",
        style: VelaText.sans14.copyWith(color: VelaColors.secondary),
      ),
      const SizedBox(height: 22),
      FilledButton(
        key: const ValueKey('report-done'),
        style: velaFilledButtonStyle(),
        onPressed: () => Navigator.of(context).pop(),
        child: const Text('Done'),
      ),
    ],
  );

  Widget _form(BuildContext context) => Column(
    crossAxisAlignment: CrossAxisAlignment.start,
    children: [
      Text('Report', style: VelaText.serif26),
      const SizedBox(height: 4),
      Text(
        "Tell us what's wrong. Reports are private.",
        style: VelaText.sans13.copyWith(color: VelaColors.muted),
      ),
      const SizedBox(height: 12),
      for (final r in ReportReason.values)
        _ReasonTile(
          reason: r,
          selected: r == _reason,
          onTap: _sending ? null : () => setState(() => _reason = r),
        ),
      if (_reason == ReportReason.copyright) _copyrightNote(context),
      const SizedBox(height: 12),
      TextField(
        key: const ValueKey('report-note'),
        controller: _note,
        enabled: !_sending,
        minLines: 2,
        maxLines: 4,
        inputFormatters: [Utf16LengthFormatter(reportNoteMaxLength)],
        style: VelaText.sans15,
        decoration: velaInputDecoration(hint: 'Add details (optional)'),
      ),
      if (_error case final e?)
        Padding(
          padding: const EdgeInsets.only(top: 10),
          child: Text(
            reportErrorMessage(e),
            key: const ValueKey('report-error'),
            style: VelaText.sans13.copyWith(color: VelaColors.danger),
          ),
        ),
      const SizedBox(height: 16),
      if (_error case final e? when isTerminalReportError(e))
        OutlinedButton(
          key: const ValueKey('report-close'),
          style: velaOutlinedButtonStyle(),
          onPressed: () => Navigator.of(context).pop(),
          child: const Text('Close'),
        )
      else
        FilledButton(
          key: const ValueKey('report-send'),
          style: velaFilledButtonStyle(),
          onPressed: _reason == null || _sending ? null : _send,
          child: Text(
            _sending
                ? 'Sending...'
                : _error == null
                ? 'Send report'
                : 'Try again',
          ),
        ),
    ],
  );

  Widget _copyrightNote(BuildContext context) => Padding(
    padding: const EdgeInsets.only(top: 8),
    child: Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(
          'If you own the copyright, a formal notice is the fastest route. '
          'You can still send this report now.',
          style: VelaText.sans13.copyWith(color: VelaColors.secondary),
        ),
        TextButton(
          key: const ValueKey('report-copyright-link'),
          style: velaTextButtonStyle(),
          onPressed: () => ref.read(legalLinkOpenerProvider)(
            context,
            legalUri(LegalDoc.dmca),
          ),
          child: const Text('Report copyright infringement'),
        ),
      ],
    ),
  );
}

class _ReasonTile extends StatelessWidget {
  const _ReasonTile({
    required this.reason,
    required this.selected,
    required this.onTap,
  });

  final ReportReason reason;
  final bool selected;
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) => Semantics(
    button: true,
    selected: selected,
    label: '${reason.label}. ${reason.hint}',
    excludeSemantics: true,
    onTap: onTap,
    child: InkWell(
      key: ValueKey('report-reason-${reason.api.value}'),
      onTap: onTap == null
          ? null
          : () {
              HapticFeedback.selectionClick();
              onTap!();
            },
      borderRadius: BorderRadius.circular(12),
      child: Container(
        constraints: const BoxConstraints(minHeight: 56),
        padding: const EdgeInsets.symmetric(horizontal: 4, vertical: 8),
        child: Row(
          children: [
            Icon(
              selected ? Icons.radio_button_checked : Icons.radio_button_off,
              size: 20,
              color: selected ? VelaColors.accent : VelaColors.faint,
            ),
            const SizedBox(width: 12),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(reason.label, style: VelaText.sans15m),
                  Text(
                    reason.hint,
                    style: VelaText.sans12.copyWith(color: VelaColors.muted),
                  ),
                ],
              ),
            ),
          ],
        ),
      ),
    ),
  );
}
