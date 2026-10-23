import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:vela_api_client/vela_api_client.dart';

import '../../core/theme/form_styles.dart';
import '../../core/theme/tokens.dart';
import 'export_flow.dart';

String _date(DateTime d) =>
    '${d.year}-${d.month.toString().padLeft(2, '0')}-${d.day.toString().padLeft(2, '0')}';

/// Request, track and download a copy of the account's data.
class ExportSection extends ConsumerWidget {
  const ExportSection({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final s = ref.watch(exportControllerProvider);
    final ctl = ref.read(exportControllerProvider.notifier);
    final job = s.job;
    final status = job?.status;
    final (text, action) = switch (status) {
      null => (
        'Get a copy of your profile, stories, replies and follows. '
            'You can ask once a day.',
        null,
      ),
      ExportJobStatusEnum.pending => (
        'Preparing your export. This can take a few minutes.',
        ('Refresh', ctl.refresh, 'export-refresh'),
      ),
      ExportJobStatusEnum.ready => (
        job!.expiresAt == null
            ? 'Your export is ready.'
            : 'Your export is ready. It stays available until '
                  '${_date(job.expiresAt!)}. Each download link works for '
                  'a few minutes.',
        ('Download', ctl.download, 'export-download'),
      ),
      ExportJobStatusEnum.expired => (
        'That export has expired. Request a new one tomorrow if you need it.',
        null,
      ),
      _ => ("The export didn't finish. Try again tomorrow.", null),
    };
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(
          text,
          key: const ValueKey('export-status'),
          style: VelaText.sans13.copyWith(color: VelaColors.muted),
        ),
        const SizedBox(height: 10),
        Row(
          children: [
            if (job == null ||
                status == ExportJobStatusEnum.expired ||
                status == ExportJobStatusEnum.failed)
              OutlinedButton(
                key: const ValueKey('export-request'),
                style: velaOutlinedButtonStyle(),
                onPressed: s.busy ? null : ctl.request,
                child: const Text('Export my data'),
              ),
            if (action != null)
              FilledButton(
                key: ValueKey(action.$3),
                style: velaFilledButtonStyle(),
                onPressed: s.busy ? null : action.$2,
                child: Text(action.$1),
              ),
            if (s.busy)
              const Padding(
                padding: EdgeInsets.only(left: 12),
                child: SizedBox(
                  width: 18,
                  height: 18,
                  child: CircularProgressIndicator(strokeWidth: 2),
                ),
              ),
          ],
        ),
        if (s.message != null)
          Padding(
            padding: const EdgeInsets.only(top: 8),
            child: Semantics(
              liveRegion: true,
              child: Text(
                s.message!,
                key: const ValueKey('export-message'),
                style: VelaText.sans13.copyWith(
                  color: s.isError ? VelaColors.danger : VelaColors.secondary,
                ),
              ),
            ),
          ),
      ],
    );
  }
}
