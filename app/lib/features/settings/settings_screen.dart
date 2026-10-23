import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/api/api_providers.dart' show sessionProvider;
import '../../core/branding.dart';
import '../../core/config.dart';
import '../../core/theme/form_styles.dart';
import '../../core/theme/tokens.dart';
import '../auth/sign_out.dart';
import '../onboarding/legal_links.dart';
import '../portfolio/portfolio_data.dart';
import 'delete_account_flow.dart';
import 'export_section.dart';

const accountMethodsNote =
    'Each sign-in method (Apple, Google, email) is its own account. '
    'They are never merged.';

/// Account, data, legal and support. Gated by the router.
class SettingsScreen extends ConsumerWidget {
  const SettingsScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final handle = ref.watch(meProvider).value?.profile?.handle;
    final open = ref.read(legalLinkOpenerProvider);
    // A pushed page is not redirected by the router: leave when the session
    // ends (sign out, or account deletion).
    ref.listen(sessionProvider.select((s) => s.value != null), (was, now) {
      if (was == true && !now) context.go('/');
    });
    return Scaffold(
      body: SafeArea(
        child: Column(
          children: [
            SizedBox(
              height: 60,
              child: Row(
                children: [
                  IconButton(
                    key: const ValueKey('settings-back'),
                    tooltip: 'Back',
                    constraints: const BoxConstraints(
                      minWidth: 48,
                      minHeight: 48,
                    ),
                    onPressed: () =>
                        context.canPop() ? context.pop() : context.go('/'),
                    icon: const Icon(Icons.arrow_back, color: VelaColors.ink),
                  ),
                  Semantics(
                    header: true,
                    child: Text('Settings', style: VelaText.serif24),
                  ),
                ],
              ),
            ),
            Expanded(
              child: ListView(
                padding: const EdgeInsets.fromLTRB(20, 4, 20, 32),
                children: [
                  const _Heading('ACCOUNT'),
                  Text(
                    handle == null ? 'Signed in.' : 'Signed in as @$handle.',
                    key: const ValueKey('settings-account'),
                    style: VelaText.sans15.copyWith(color: VelaColors.ink),
                  ),
                  const SizedBox(height: 4),
                  Text(
                    accountMethodsNote,
                    style: VelaText.sans13.copyWith(color: VelaColors.muted),
                  ),
                  const SizedBox(height: 12),
                  OutlinedButton(
                    key: const ValueKey('settings-sign-out'),
                    style: velaOutlinedButtonStyle(),
                    onPressed: () => ref.read(signOutProvider)(),
                    child: const Text('Sign out'),
                  ),
                  const _Heading('YOUR DATA'),
                  const ExportSection(),
                  const SizedBox(height: 16),
                  OutlinedButton(
                    key: const ValueKey('settings-delete'),
                    style: velaOutlinedButtonStyle().copyWith(
                      foregroundColor: const WidgetStatePropertyAll(
                        VelaColors.danger,
                      ),
                    ),
                    onPressed: () =>
                        showDeleteAccountDialog(context, handle: handle),
                    child: const Text('Delete account'),
                  ),
                  const _Heading('LEGAL'),
                  _LinkRow(
                    'Privacy policy',
                    'link-privacy',
                    () => open(context, legalUri(LegalDoc.privacy)),
                  ),
                  _LinkRow(
                    'Terms of service',
                    'link-terms',
                    () => open(context, legalUri(LegalDoc.terms)),
                  ),
                  _LinkRow(
                    'Content policy',
                    'link-content',
                    () => open(context, legalUri(LegalDoc.content)),
                  ),
                  _LinkRow(
                    'Copyright and DMCA notices',
                    'link-dmca',
                    () => open(context, legalUri(LegalDoc.dmca)),
                  ),
                  const _Heading('SUPPORT'),
                  _LinkRow(
                    'Contact support',
                    'link-support',
                    () => _contactSupport(context, ref),
                  ),
                  const _Heading('ABOUT'),
                  Text(
                    '${Branding.appName} ${AppConfig.appVersion}',
                    key: const ValueKey('settings-version'),
                    style: VelaText.sans13.copyWith(color: VelaColors.muted),
                  ),
                ],
              ),
            ),
          ],
        ),
      ),
    );
  }

  static Future<void> _contactSupport(
    BuildContext context,
    WidgetRef ref,
  ) async {
    final messenger = ScaffoldMessenger.of(context);
    final uri = Uri(scheme: 'mailto', path: AppConfig.supportEmail);
    final opened = await ref.read(legalUrlLauncherProvider)(uri);
    if (!opened) {
      messenger.showSnackBar(
        SnackBar(
          content: Text(
            'No email app found. Write to ${AppConfig.supportEmail}.',
          ),
        ),
      );
    }
  }
}

class _Heading extends StatelessWidget {
  const _Heading(this.text);
  final String text;

  @override
  Widget build(BuildContext context) => Padding(
    padding: const EdgeInsets.only(top: 28, bottom: 10),
    child: Semantics(
      header: true,
      child: Text(text, style: VelaText.monoKicker),
    ),
  );
}

class _LinkRow extends StatelessWidget {
  const _LinkRow(this.label, this.id, this.onTap);
  final String label;
  final String id;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) => Semantics(
    button: true,
    label: label,
    excludeSemantics: true,
    onTap: onTap,
    child: InkWell(
      key: ValueKey(id),
      onTap: onTap,
      child: ConstrainedBox(
        constraints: const BoxConstraints(minHeight: 48),
        child: Align(
          alignment: Alignment.centerLeft,
          child: Text(
            label,
            style: VelaText.sans15.copyWith(color: VelaColors.secondary),
          ),
        ),
      ),
    ),
  );
}
