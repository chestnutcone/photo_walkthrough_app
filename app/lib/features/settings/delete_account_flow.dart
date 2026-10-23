import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_exception.dart';
import '../../core/api/api_providers.dart';
import '../../core/auth/auth_controller.dart' show SessionExpiredException;
import '../../core/auth/local_data.dart';
import '../../core/theme/form_styles.dart';
import '../../core/theme/tokens.dart';

const deleteWord = 'DELETE';

const deleteExplanation =
    'This permanently deletes your account and everything in it: your '
    'stories and photos, your replies, your follows and followers, your '
    'notifications and your messages. It cannot be undone. Copies in backups '
    'are removed within 30 days. Content held for a legal or safety '
    'investigation may be kept as the privacy policy describes.';

/// True when [typed] is the word DELETE (exactly) or the account's handle.
bool deleteConfirmed(String typed, String? handle) {
  final t = typed.trim();
  if (t == deleteWord) return true;
  return handle != null && handle.isNotEmpty && t.toLowerCase() == handle;
}

/// Deletes the account on the server; only after it succeeds is the device
/// wiped and the session ended (the router then shows the feed as a guest).
/// A refused request changes nothing local.
///
/// A lost response is the one ambiguous case: the server may have deleted the
/// account already. The retry then meets 401 or 403 `account_deleted` (tokens
/// are revoked), which means the account is gone, so it counts as success.
final deleteAccountProvider = Provider<Future<void> Function()>((ref) {
  return () async {
    try {
      await apiGuard(() => ref.read(apiProvider).getMeApi().deleteAccount());
    } on ApiException catch (e) {
      if (!_accountAlreadyGone(e)) rethrow;
    } on SessionExpiredException {
      // Refresh failed because the account no longer exists.
    }
    await ref.read(wipeLocalDataProvider)();
  };
});

bool _accountAlreadyGone(ApiException e) =>
    e.code == 'account_deleted' ||
    e.statusCode == 401 ||
    e.code == 'unauthorized';

Future<void> showDeleteAccountDialog(BuildContext context, {String? handle}) =>
    showDialog<void>(
      context: context,
      barrierDismissible: false,
      builder: (_) => DeleteAccountDialog(handle: handle),
    );

class DeleteAccountDialog extends ConsumerStatefulWidget {
  const DeleteAccountDialog({super.key, this.handle});
  final String? handle;

  @override
  ConsumerState<DeleteAccountDialog> createState() => _DeleteState();
}

class _DeleteState extends ConsumerState<DeleteAccountDialog> {
  final _typed = TextEditingController();
  bool _busy = false;
  String? _error;

  @override
  void dispose() {
    _typed.dispose();
    super.dispose();
  }

  Future<void> _delete() async {
    if (_busy) return; // double-tap guard
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      await ref.read(deleteAccountProvider)();
      if (mounted) Navigator.of(context).pop();
    } on Object catch (e) {
      // Any failure ends the busy state; the dialog never gets stuck.
      if (!mounted) return;
      setState(() {
        _busy = false;
        _error = e is ApiException && e.code == 'network_error'
            ? "Can't reach the server. Your account was not deleted."
            : "Couldn't delete your account. Nothing was deleted; try again.";
      });
    }
  }

  @override
  Widget build(BuildContext context) {
    final ok = deleteConfirmed(_typed.text, widget.handle);
    final handleHint = widget.handle == null ? '' : ' or @${widget.handle}';
    return PopScope(
      canPop: !_busy,
      child: AlertDialog(
        backgroundColor: VelaColors.sheet,
        title: Semantics(
          header: true,
          child: Text('Delete account', style: VelaText.serif24),
        ),
        content: SingleChildScrollView(
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(
                deleteExplanation,
                key: const ValueKey('delete-explanation'),
                style: VelaText.sans14.copyWith(color: VelaColors.secondary),
              ),
              const SizedBox(height: 14),
              Semantics(
                label: 'Type $deleteWord$handleHint to confirm',
                textField: true,
                child: TextField(
                  key: const ValueKey('delete-confirm-field'),
                  controller: _typed,
                  enabled: !_busy,
                  autocorrect: false,
                  enableSuggestions: false,
                  style: VelaText.sans15,
                  decoration: velaInputDecoration(
                    hint: 'Type $deleteWord$handleHint',
                  ),
                  onChanged: (_) => setState(() {}),
                ),
              ),
              if (_error != null) ...[
                const SizedBox(height: 10),
                Semantics(
                  liveRegion: true,
                  child: Text(
                    _error!,
                    key: const ValueKey('delete-error'),
                    style: VelaText.sans13.copyWith(color: VelaColors.danger),
                  ),
                ),
              ],
            ],
          ),
        ),
        actions: [
          TextButton(
            key: const ValueKey('delete-cancel'),
            style: velaTextButtonStyle(),
            onPressed: _busy ? null : () => Navigator.of(context).pop(),
            child: const Text('Cancel'),
          ),
          FilledButton(
            key: const ValueKey('delete-confirm'),
            style: velaFilledButtonStyle().copyWith(
              backgroundColor: WidgetStateProperty.resolveWith(
                (s) => s.contains(WidgetState.disabled)
                    ? VelaColors.input
                    : VelaColors.danger,
              ),
            ),
            onPressed: ok && !_busy ? _delete : null,
            child: _busy
                ? const SizedBox(
                    width: 18,
                    height: 18,
                    child: CircularProgressIndicator(strokeWidth: 2),
                  )
                : const Text('Delete forever'),
          ),
        ],
      ),
    );
  }
}
