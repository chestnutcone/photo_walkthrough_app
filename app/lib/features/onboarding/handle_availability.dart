import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/foundation.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_exception.dart';
import '../../core/api/api_providers.dart';
import 'handle_rules.dart';

enum HandleStatus { empty, invalid, checking, available, taken, unknown }

/// What is known about [handle] (always normalised). The status belongs to
/// that handle only.
@immutable
class HandleCheck {
  const HandleCheck(this.handle, this.status, [this.problem]);

  static const none = HandleCheck('', HandleStatus.empty);

  final String handle;
  final HandleStatus status;
  final HandleProblem? problem;
}

const _debounce = Duration(milliseconds: 400);

final handleAvailabilityProvider =
    NotifierProvider.autoDispose<HandleAvailability, HandleCheck>(
      HandleAvailability.new,
    );

/// Live handle validation. The API has no availability endpoint, so a handle
/// is free when `GET /v1/artists/{handle}` is 404; any other failure is
/// "unknown", never "available". `completeOnboarding` still enforces
/// uniqueness (409 handle_taken), which covers hidden or blocked profiles
/// that look absent here.
///
/// Every answer is applied only if it is still the latest request: a sequence
/// number guards against slow, out-of-order responses (cancel tokens are
/// best effort and not relied on).
class HandleAvailability extends Notifier<HandleCheck> {
  Timer? _timer;
  CancelToken? _cancel;
  int _seq = 0;

  @override
  HandleCheck build() {
    ref.onDispose(() {
      _timer?.cancel();
      _cancel?.cancel();
      _seq++;
    });
    return HandleCheck.none;
  }

  void onChanged(String raw, {Duration debounce = _debounce}) {
    final handle = HandleRules.normalize(raw);
    final settled =
        state.status == HandleStatus.available ||
        state.status == HandleStatus.taken ||
        state.status == HandleStatus.checking;
    if (handle == state.handle && settled) return;

    _timer?.cancel();
    _cancel?.cancel();
    final seq = ++_seq;
    if (handle.isEmpty) {
      state = HandleCheck.none;
      return;
    }
    final problem = HandleRules.problem(handle);
    if (problem != null) {
      state = HandleCheck(handle, HandleStatus.invalid, problem);
      return;
    }
    state = HandleCheck(handle, HandleStatus.checking);
    _timer = Timer(debounce, () => _lookup(handle, seq));
  }

  Future<void> _lookup(String handle, int seq) async {
    final cancel = _cancel = CancelToken();
    var status = HandleStatus.unknown;
    try {
      await apiGuard(
        () => ref
            .read(apiProvider)
            .getArtistsApi()
            .getArtist(handle: handle, cancelToken: cancel),
      );
      status = HandleStatus.taken;
    } on ApiException catch (e) {
      if (e.statusCode == 404) status = HandleStatus.available;
    }
    if (!ref.mounted || seq != _seq) return;
    state = HandleCheck(handle, status);
  }
}
