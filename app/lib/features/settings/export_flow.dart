import 'dart:async';

import 'package:flutter/foundation.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:vela_api_client/vela_api_client.dart';

import '../../core/api/api_exception.dart';
import '../../core/api/api_providers.dart';
import '../onboarding/legal_links.dart';

/// How often a pending export is checked. Tests shorten it.
final exportPollIntervalProvider = Provider<Duration>(
  (_) => const Duration(seconds: 5),
);

/// Pending exports are polled this many times, then left to "Refresh".
const int maxExportPolls = 60;

/// An export as shown. The signed download URL is deliberately not part of
/// this: it is fetched fresh when the user taps Download, opened once in the
/// external browser and never stored.
@immutable
class ExportView {
  const ExportView({required this.id, required this.status, this.expiresAt});
  final String id;
  final ExportJobStatusEnum status;
  final DateTime? expiresAt;
}

@immutable
class ExportState {
  const ExportState({
    this.job,
    this.busy = false,
    this.message,
    this.isError = false,
  });
  final ExportView? job;
  final bool busy;

  /// Server or local message for the user (for example the daily limit).
  final String? message;
  final bool isError;

  ExportState copyWith({
    ExportView? job,
    bool? busy,
    String? message,
    bool clearMessage = false,
    bool isError = false,
  }) => ExportState(
    job: job ?? this.job,
    busy: busy ?? this.busy,
    message: clearMessage ? null : message ?? this.message,
    isError: isError,
  );
}

final exportControllerProvider =
    NotifierProvider.autoDispose<ExportController, ExportState>(
      ExportController.new,
    );

class ExportController extends Notifier<ExportState> {
  Timer? _timer;
  int _polls = 0;

  @override
  ExportState build() {
    ref.watch(sessionEpochProvider);
    ref.onDispose(() => _timer?.cancel());
    return const ExportState();
  }

  MeApi get _api => ref.read(apiProvider).getMeApi();

  /// Asks for an export. The server allows one per day; its message is shown
  /// as is when it refuses.
  Future<void> request() async {
    if (state.busy) return;
    state = state.copyWith(busy: true, clearMessage: true);
    try {
      final job = _unwrap(await apiGuard(() => _api.requestExport()));
      if (!ref.mounted) return;
      state = ExportState(job: _view(job));
      _poll();
    } on ApiException catch (e) {
      if (!ref.mounted) return;
      state = state.copyWith(busy: false, message: _failure(e), isError: true);
    }
  }

  /// Re-reads the current export (the Refresh button).
  Future<void> refresh() async {
    final id = state.job?.id;
    if (id == null || state.busy) return;
    state = state.copyWith(busy: true, clearMessage: true);
    try {
      final job = _unwrap(await apiGuard(() => _api.getExport(id: id)));
      if (!ref.mounted) return;
      state = ExportState(job: _view(job));
      if (job.status == ExportJobStatusEnum.pending) _poll();
    } on ApiException catch (e) {
      if (!ref.mounted) return;
      state = state.copyWith(busy: false, message: _failure(e), isError: true);
    }
  }

  /// Fetches a fresh short-lived link and opens it in the external browser.
  Future<void> download() async {
    final id = state.job?.id;
    if (id == null || state.busy) return;
    state = state.copyWith(busy: true, clearMessage: true);
    var opened = false;
    String? failure;
    try {
      final job = _unwrap(await apiGuard(() => _api.getExport(id: id)));
      final url = job.downloadUrl;
      final uri = url == null ? null : Uri.tryParse(url);
      if (job.status == ExportJobStatusEnum.ready &&
          uri != null &&
          isLaunchableLegalUri(uri)) {
        opened = await ref.read(legalUrlLauncherProvider)(uri);
      }
      if (!ref.mounted) return;
      state = ExportState(job: _view(job));
    } on ApiException catch (e) {
      failure = _failure(e);
    }
    if (!ref.mounted) return;
    state = state.copyWith(
      busy: false,
      message: opened
          ? null
          : (failure ?? "Couldn't open the download. Refresh and try again."),
      isError: !opened,
    );
  }

  void _poll() {
    _timer?.cancel();
    _polls = 0;
    _timer = Timer.periodic(ref.read(exportPollIntervalProvider), (t) async {
      if (++_polls > maxExportPolls ||
          state.job?.status != ExportJobStatusEnum.pending) {
        t.cancel();
        return;
      }
      final id = state.job?.id;
      if (id == null) return;
      try {
        final job = _unwrap(await apiGuard(() => _api.getExport(id: id)));
        if (!ref.mounted) return;
        state = state.copyWith(job: _view(job), busy: false);
        if (job.status != ExportJobStatusEnum.pending) t.cancel();
      } on ApiException {
        // Keep waiting; the Refresh button reports real errors.
      }
    });
    state = state.copyWith(busy: false);
  }

  ExportJob _unwrap(dynamic res) => unwrapEnvelope(
    success: res.data?.success ?? false,
    data: res.data?.data as ExportJob?,
    error: res.data?.error,
  );

  ExportView _view(ExportJob j) =>
      ExportView(id: j.id, status: j.status, expiresAt: j.expiresAt?.toLocal());

  String _failure(ApiException e) {
    if (e.code == 'rate_limited' && e.message.isNotEmpty) return e.message;
    return switch (e.code) {
      'network_error' => "Can't reach the server. Check your connection.",
      'rate_limited' => 'You can request one export per day.',
      _ => "Couldn't complete that. Try again later.",
    };
  }
}
