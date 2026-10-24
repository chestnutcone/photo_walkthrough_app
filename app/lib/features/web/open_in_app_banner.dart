import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/branding.dart';
import '../../core/theme/tokens.dart';
import '../onboarding/legal_links.dart';
import 'web_viewer_config.dart';

/// Top strip of the web viewer: sign-in is not offered on the web, so this is
/// the only call to action. "Open in app" always shows; each store button
/// shows only when its URL is configured.
class OpenInAppBanner extends ConsumerWidget {
  const OpenInAppBanner({super.key, required this.workId});

  final String workId;

  static const double height = 44;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final config = ref.watch(webViewerConfigProvider);
    final launch = ref.watch(legalUrlLauncherProvider);
    return Material(
      color: const Color(0xE60F0E0D),
      child: SizedBox(
        height: height,
        child: Padding(
          padding: const EdgeInsets.symmetric(horizontal: 12),
          child: Row(
            children: [
              Expanded(
                child: Text(
                  'Better in the ${Branding.appName} app',
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: VelaText.sans12m,
                ),
              ),
              _BannerButton(
                key: const ValueKey('open-in-app'),
                label: 'Open in app',
                filled: true,
                onTap: () => launch(config.deepLinkFor(workId)),
              ),
              if (config.appStoreUri case final uri?)
                _BannerButton(
                  key: const ValueKey('store-app-store'),
                  label: 'iOS',
                  semanticLabel: 'App Store',
                  onTap: () => launch(uri),
                ),
              if (config.playStoreUri case final uri?)
                _BannerButton(
                  key: const ValueKey('store-google-play'),
                  label: 'Android',
                  semanticLabel: 'Google Play',
                  onTap: () => launch(uri),
                ),
            ],
          ),
        ),
      ),
    );
  }
}

class _BannerButton extends StatelessWidget {
  const _BannerButton({
    super.key,
    required this.label,
    required this.onTap,
    this.filled = false,
    this.semanticLabel,
  });

  final String label;
  final String? semanticLabel;
  final VoidCallback onTap;
  final bool filled;

  @override
  Widget build(BuildContext context) => Padding(
    padding: const EdgeInsets.only(left: 6),
    child: Semantics(
      button: true,
      label: semanticLabel ?? label,
      excludeSemantics: true,
      onTap: onTap,
      child: GestureDetector(
        onTap: onTap,
        behavior: HitTestBehavior.opaque,
        child: Container(
          padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 6),
          decoration: BoxDecoration(
            color: filled ? VelaColors.ink : Colors.transparent,
            borderRadius: BorderRadius.circular(99),
            border: filled ? null : Border.all(color: VelaColors.faint),
          ),
          child: Text(
            label,
            style: VelaText.sans12m.copyWith(
              color: filled ? VelaColors.background : VelaColors.ink,
            ),
          ),
        ),
      ),
    ),
  );
}
