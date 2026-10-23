import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/api/api_providers.dart' show sessionProvider;
import '../../core/theme/form_styles.dart';
import '../../core/theme/tokens.dart';
import '../onboarding/handle_availability.dart';
import '../onboarding/handle_rules.dart';
import '../onboarding/profile_setup_screen.dart' show HandleInputFormatter;
import '../portfolio/portfolio_data.dart';
import 'edit_dialogs.dart';
import 'edit_fields.dart';
import 'profile_edit_controller.dart';
import 'services_editor.dart';
import 'shows_editor.dart';

/// Edit the signed-in account's public profile. Gated by the router; the
/// form mounts once `/v1/me` has loaded.
class EditProfileScreen extends ConsumerWidget {
  const EditProfileScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final me = ref.watch(meProvider);
    ref.listen(sessionProvider.select((s) => s.value != null), (was, now) {
      if (was == true && !now) context.go('/');
    });
    return Scaffold(
      body: SafeArea(
        child: me.when(
          data: (m) => m?.profile == null
              ? const _Message('Finish setting up your account first.')
              : const _EditForm(),
          loading: () => const Center(child: CircularProgressIndicator()),
          error: (_, _) => _Message(
            "Couldn't load your profile.",
            retry: () => ref.invalidate(meProvider),
          ),
        ),
      ),
    );
  }
}

class _Message extends StatelessWidget {
  const _Message(this.text, {this.retry});
  final String text;
  final VoidCallback? retry;

  @override
  Widget build(BuildContext context) => Center(
    child: Column(
      mainAxisSize: MainAxisSize.min,
      children: [
        Text(
          text,
          style: VelaText.sans14.copyWith(color: VelaColors.secondary),
        ),
        if (retry != null)
          TextButton(
            key: const ValueKey('edit-retry'),
            style: velaTextButtonStyle(),
            onPressed: retry,
            child: const Text('Retry'),
          ),
        TextButton(
          style: velaTextButtonStyle(),
          onPressed: () => context.canPop() ? context.pop() : context.go('/'),
          child: const Text('Back'),
        ),
      ],
    ),
  );
}

class _EditForm extends ConsumerStatefulWidget {
  const _EditForm();

  @override
  ConsumerState<_EditForm> createState() => _EditFormState();
}

class _EditFormState extends ConsumerState<_EditForm> {
  late final TextEditingController _name;
  late final TextEditingController _handle;
  late final TextEditingController _bio;
  late final TextEditingController _city;
  late final TextEditingController _email;
  late final TextEditingController _website;

  @override
  void initState() {
    super.initState();
    final f = ref.read(profileEditProvider).form;
    _name = TextEditingController(text: f.displayName);
    _handle = TextEditingController(text: f.handle);
    _bio = TextEditingController(text: f.bio);
    _city = TextEditingController(text: f.city);
    _email = TextEditingController(text: f.publicEmail);
    _website = TextEditingController(text: f.website);
  }

  @override
  void dispose() {
    for (final c in [_name, _handle, _bio, _city, _email, _website]) {
      c.dispose();
    }
    super.dispose();
  }

  ProfileEditController get _ctl => ref.read(profileEditProvider.notifier);

  void _onHandle(String v) {
    _ctl.edit((f) => f.copyWith(handle: v));
    final h = HandleRules.normalize(v);
    final original = ref.read(profileEditProvider).original.handle;
    final check = ref.read(handleAvailabilityProvider.notifier);
    // Your own current handle needs no lookup (it would say "taken").
    check.onChanged(h == original ? '' : v);
  }

  Future<void> _save() async {
    final s = ref.read(profileEditProvider);
    if (s.saving) return;
    if (s.fieldErrors.isNotEmpty) return _ctl.showErrors();
    if (s.handleChanged &&
        !await confirmHandleChange(context, s.form.normalized.handle)) {
      return;
    }
    if (!mounted) return;
    FocusScope.of(context).unfocus();
    final out = await _ctl.save();
    if (!mounted || !out.saved) return;
    if (out.newHandle != null) {
      context.go('/artist/${out.newHandle}');
    } else if (context.canPop()) {
      context.pop();
    } else {
      context.go('/');
    }
  }

  Future<void> _back() async {
    final s = ref.read(profileEditProvider);
    if (s.saving) return;
    if (s.dirty && !await confirmDiscard(context)) return;
    if (!mounted) return;
    context.canPop() ? context.pop() : context.go('/');
  }

  @override
  Widget build(BuildContext context) {
    final s = ref.watch(profileEditProvider);
    final check = ref.watch(handleAvailabilityProvider);
    final errors = s.showErrors ? s.fieldErrors : const <String, String>{};
    final busy = s.saving;
    return PopScope(
      canPop: !s.dirty && !s.saving,
      onPopInvokedWithResult: (didPop, _) {
        if (!didPop) _back();
      },
      child: Column(
        children: [
          _Header(
            onBack: _back,
            onSave: s.canSave(check) ? _save : null,
            saving: busy,
          ),
          Expanded(
            child: SingleChildScrollView(
              keyboardDismissBehavior: ScrollViewKeyboardDismissBehavior.onDrag,
              padding: const EdgeInsets.fromLTRB(20, 4, 20, 32),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  AvatarEditor(
                    state: s,
                    name: s.form.displayName,
                    currentUrl: ref.watch(meProvider).value?.profile?.avatarUrl,
                    onTap: busy ? null : _ctl.pickAvatar,
                  ),
                  const SizedBox(height: 20),
                  LabeledField(
                    label: 'DISPLAY NAME',
                    child: TextField(
                      key: const ValueKey('edit-name'),
                      controller: _name,
                      enabled: !busy,
                      textCapitalization: TextCapitalization.words,
                      textInputAction: TextInputAction.next,
                      style: VelaText.sans15,
                      decoration: velaInputDecoration(
                        hint: 'Your name',
                        errorText: errors['displayName'],
                      ),
                      onChanged: (v) =>
                          _ctl.edit((f) => f.copyWith(displayName: v)),
                    ),
                  ),
                  LabeledField(
                    label: 'HANDLE',
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        TextField(
                          key: const ValueKey('edit-handle'),
                          controller: _handle,
                          enabled: !busy,
                          autocorrect: false,
                          enableSuggestions: false,
                          keyboardType: TextInputType.visiblePassword,
                          textInputAction: TextInputAction.next,
                          inputFormatters: [
                            HandleInputFormatter(),
                            LengthLimitingTextInputFormatter(
                              HandleRules.maxLength + 1,
                            ),
                          ],
                          style: VelaText.sans15,
                          decoration: velaInputDecoration(hint: 'yourname')
                              .copyWith(
                                prefixText: '@',
                                prefixStyle: VelaText.sans15.copyWith(
                                  color: VelaColors.muted,
                                ),
                              ),
                          onChanged: _onHandle,
                        ),
                        const SizedBox(height: 6),
                        HandleStatusLine(state: s, check: check),
                      ],
                    ),
                  ),
                  LabeledField(
                    label: 'BIO',
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        TextField(
                          key: const ValueKey('edit-bio'),
                          controller: _bio,
                          enabled: !busy,
                          minLines: 3,
                          maxLines: 6,
                          textInputAction: TextInputAction.newline,
                          keyboardType: TextInputType.multiline,
                          style: VelaText.sans15,
                          decoration: velaInputDecoration(
                            hint: 'A few lines about your work',
                            errorText: s.fieldErrors['bio'],
                          ),
                          onChanged: (v) =>
                              _ctl.edit((f) => f.copyWith(bio: v)),
                        ),
                        BioCounter(text: s.form.bio),
                      ],
                    ),
                  ),
                  LabeledField(
                    label: 'CITY',
                    child: TextField(
                      key: const ValueKey('edit-city'),
                      controller: _city,
                      enabled: !busy,
                      textCapitalization: TextCapitalization.words,
                      textInputAction: TextInputAction.next,
                      style: VelaText.sans15,
                      decoration: velaInputDecoration(
                        hint: 'Where you work',
                        errorText: errors['city'],
                      ),
                      onChanged: (v) => _ctl.edit((f) => f.copyWith(city: v)),
                    ),
                  ),
                  LabeledField(
                    label: 'PUBLIC CONTACT EMAIL (OPTIONAL)',
                    child: TextField(
                      key: const ValueKey('edit-email'),
                      controller: _email,
                      enabled: !busy,
                      keyboardType: TextInputType.emailAddress,
                      autocorrect: false,
                      textInputAction: TextInputAction.next,
                      style: VelaText.sans15,
                      decoration: velaInputDecoration(
                        hint: 'hello@yourstudio.com',
                        errorText: s.fieldErrors['publicEmail'],
                        helperText: publicEmailNote,
                      ),
                      onChanged: (v) =>
                          _ctl.edit((f) => f.copyWith(publicEmail: v)),
                    ),
                  ),
                  LabeledField(
                    label: 'WEBSITE',
                    child: TextField(
                      key: const ValueKey('edit-website'),
                      controller: _website,
                      enabled: !busy,
                      keyboardType: TextInputType.url,
                      autocorrect: false,
                      textInputAction: TextInputAction.done,
                      style: VelaText.sans15,
                      decoration: velaInputDecoration(
                        hint: 'https://yourstudio.com',
                        errorText: s.fieldErrors['website'],
                      ),
                      onChanged: (v) =>
                          _ctl.edit((f) => f.copyWith(website: v)),
                    ),
                  ),
                  const SizedBox(height: 6),
                  ServicesEditor(
                    items: s.form.services,
                    enabled: !busy,
                    onChanged: (v) => _ctl.edit((f) => f.copyWith(services: v)),
                  ),
                  const SizedBox(height: 22),
                  ShowsEditor(
                    items: s.form.shows,
                    enabled: !busy,
                    onChanged: (v) => _ctl.edit((f) => f.copyWith(shows: v)),
                  ),
                  if (s.error != null) ...[
                    const SizedBox(height: 18),
                    Semantics(
                      liveRegion: true,
                      child: Text(
                        s.error!,
                        key: const ValueKey('edit-error'),
                        style: VelaText.sans13.copyWith(
                          color: VelaColors.danger,
                        ),
                      ),
                    ),
                  ],
                ],
              ),
            ),
          ),
        ],
      ),
    );
  }
}

class _Header extends StatelessWidget {
  const _Header({
    required this.onBack,
    required this.onSave,
    required this.saving,
  });
  final VoidCallback onBack;
  final VoidCallback? onSave;
  final bool saving;

  @override
  Widget build(BuildContext context) => SizedBox(
    height: 60,
    child: Row(
      children: [
        IconButton(
          key: const ValueKey('edit-back'),
          tooltip: 'Back',
          constraints: const BoxConstraints(minWidth: 48, minHeight: 48),
          onPressed: saving ? null : onBack,
          icon: const Icon(Icons.arrow_back, color: VelaColors.ink),
        ),
        Expanded(
          child: Semantics(
            header: true,
            child: Text('Edit profile', style: VelaText.serif24),
          ),
        ),
        Padding(
          padding: const EdgeInsets.only(right: 16),
          child: FilledButton(
            key: const ValueKey('edit-save'),
            style: velaFilledButtonStyle(),
            onPressed: onSave,
            child: saving
                ? const SizedBox(
                    width: 18,
                    height: 18,
                    child: CircularProgressIndicator(strokeWidth: 2),
                  )
                : const Text('Save'),
          ),
        ),
      ],
    ),
  );
}
