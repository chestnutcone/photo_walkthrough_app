import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:vela_api_client/vela_api_client.dart';

import '../../core/theme/tokens.dart';
import 'portfolio_format.dart';

const _copiedFor = Duration(seconds: 2);

bool _has(String? s) => s != null && s.trim().isNotEmpty;

/// About & contact. Shows only what the artist chose to publish: the public
/// contact email is the one email ever shown, never the login email.
class AboutTab extends StatefulWidget {
  const AboutTab({
    super.key,
    required this.profile,
    required this.isOwn,
    required this.onContact,
  });

  final Profile profile;
  final bool isOwn;
  final VoidCallback onContact;

  @override
  State<AboutTab> createState() => _AboutTabState();
}

class _AboutTabState extends State<AboutTab> {
  String? _copiedKey;
  Timer? _reset;

  @override
  void dispose() {
    _reset?.cancel();
    super.dispose();
  }

  Future<void> _copy(String key, String value) async {
    await Clipboard.setData(ClipboardData(text: value));
    if (!mounted) return;
    setState(() => _copiedKey = key);
    _reset?.cancel();
    _reset = Timer(_copiedFor, () {
      if (mounted) setState(() => _copiedKey = null);
    });
  }

  @override
  Widget build(BuildContext context) {
    final p = widget.profile;
    final services = p.services ?? const <ServiceItem>[];
    final shows = p.shows ?? const <ShowItem>[];
    final rows = <Widget>[
      if (_has(p.publicEmail))
        _ContactRow(
          rowKey: const ValueKey('copy-email'),
          label: 'Email',
          value: p.publicEmail!.trim(),
          action: _copiedKey == 'email' ? 'Copied' : 'Copy',
          highlight: _copiedKey == 'email',
          semanticAction: 'Copy email address',
          onTap: () => _copy('email', p.publicEmail!.trim()),
        ),
      if (_has(p.website))
        _ContactRow(
          rowKey: const ValueKey('copy-website'),
          label: 'Website',
          value: p.website!.trim(),
          action: _copiedKey == 'website' ? 'Copied' : 'Copy',
          highlight: _copiedKey == 'website',
          semanticAction: 'Copy website address',
          onTap: () => _copy('website', p.website!.trim()),
        ),
      if (_has(p.social))
        _ContactRow(
          rowKey: const ValueKey('copy-social'),
          label: 'Social',
          value: p.social!.trim(),
          action: _copiedKey == 'social' ? 'Copied' : 'Copy',
          highlight: _copiedKey == 'social',
          semanticAction: 'Copy social handle',
          onTap: () => _copy('social', p.social!.trim()),
        ),
      if (_has(p.studio)) _ContactRow(label: 'Studio', value: p.studio!.trim()),
      if (!widget.isOwn)
        _ContactRow(
          rowKey: const ValueKey('contact-write'),
          label: 'Message',
          value: 'Send an inquiry',
          action: 'Write →',
          semanticAction: 'Write to ${firstName(p.displayName)}',
          onTap: widget.onContact,
        ),
    ];
    return Padding(
      padding: const EdgeInsets.fromLTRB(20, 22, 20, 32),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          if (services.isNotEmpty) ...[
            const _Kicker('AVAILABLE FOR'),
            for (final s in services) _ServiceRow(service: s),
            const SizedBox(height: 26),
          ],
          if (rows.isNotEmpty) ...[
            const _Kicker('CONTACT'),
            ...rows,
            if (_has(p.replyTime))
              Padding(
                padding: const EdgeInsets.only(top: 10),
                child: Text(
                  'Usually replies within ${p.replyTime!.trim()}.',
                  style: VelaText.sans13.copyWith(color: VelaColors.muted),
                ),
              ),
            const SizedBox(height: 26),
          ],
          if (shows.isNotEmpty) ...[
            const _Kicker('EXHIBITIONS & PRESS'),
            for (final s in shows) _ShowRow(show: s),
            const SizedBox(height: 26),
          ],
          if (!widget.isOwn)
            FilledButton(
              key: const ValueKey('contact-send-inquiry'),
              onPressed: widget.onContact,
              style: FilledButton.styleFrom(
                minimumSize: const Size.fromHeight(48),
                backgroundColor: VelaColors.ink,
                foregroundColor: VelaColors.background,
                shape: RoundedRectangleBorder(
                  borderRadius: BorderRadius.circular(12),
                ),
              ),
              child: Text(
                'Send ${firstName(p.displayName)} an inquiry',
                style: VelaText.sans14m.copyWith(color: VelaColors.background),
              ),
            ),
        ],
      ),
    );
  }
}

class _Kicker extends StatelessWidget {
  const _Kicker(this.text);
  final String text;

  @override
  Widget build(BuildContext context) => Padding(
    padding: const EdgeInsets.only(bottom: 6),
    child: Semantics(header: true, child: Text(text, style: VelaText.mono11)),
  );
}

class _ServiceRow extends StatelessWidget {
  const _ServiceRow({required this.service});
  final ServiceItem service;

  @override
  Widget build(BuildContext context) => Container(
    padding: const EdgeInsets.symmetric(vertical: 13),
    decoration: const BoxDecoration(
      border: Border(bottom: BorderSide(color: VelaColors.hairline)),
    ),
    child: Row(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Expanded(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(service.title, style: VelaText.sans15m),
              const SizedBox(height: 3),
              Text(
                service.description,
                style: VelaText.sans13.copyWith(
                  color: VelaColors.muted,
                  height: 1.4,
                ),
              ),
            ],
          ),
        ),
        const SizedBox(width: 16),
        Padding(
          padding: const EdgeInsets.only(top: 2),
          child: Text(
            service.price,
            style: VelaText.mono12.copyWith(color: VelaColors.secondary),
          ),
        ),
      ],
    ),
  );
}

class _ShowRow extends StatelessWidget {
  const _ShowRow({required this.show});
  final ShowItem show;

  @override
  Widget build(BuildContext context) => Container(
    padding: const EdgeInsets.symmetric(vertical: 11),
    decoration: const BoxDecoration(
      border: Border(bottom: BorderSide(color: VelaColors.hairline)),
    ),
    child: Row(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        SizedBox(
          width: 52,
          child: Text(
            show.year,
            style: VelaText.mono12.copyWith(color: VelaColors.secondary),
          ),
        ),
        Expanded(child: Text(show.text, style: VelaText.sans14)),
      ],
    ),
  );
}

class _ContactRow extends StatelessWidget {
  const _ContactRow({
    this.rowKey,
    required this.label,
    required this.value,
    this.action,
    this.highlight = false,
    this.semanticAction,
    this.onTap,
  });

  final Key? rowKey;
  final String label;
  final String value;
  final String? action;
  final bool highlight;
  final String? semanticAction;
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) {
    final content = Container(
      constraints: const BoxConstraints(minHeight: 44),
      padding: const EdgeInsets.symmetric(vertical: 13),
      decoration: const BoxDecoration(
        border: Border(bottom: BorderSide(color: VelaColors.hairline)),
      ),
      child: Row(
        children: [
          SizedBox(
            width: 72,
            child: Text(
              label,
              style: VelaText.sans12.copyWith(color: VelaColors.faint),
            ),
          ),
          Expanded(child: Text(value, style: VelaText.sans14m)),
          if (action != null) ...[
            const SizedBox(width: 12),
            Text(
              action!,
              style: VelaText.sans12m.copyWith(
                color: highlight ? VelaColors.accent : VelaColors.muted,
              ),
            ),
          ],
        ],
      ),
    );
    if (onTap == null) {
      return Semantics(
        label: '$label: $value',
        excludeSemantics: true,
        child: content,
      );
    }
    return Semantics(
      button: true,
      label: '$label: $value. ${semanticAction ?? action}',
      excludeSemantics: true,
      onTap: onTap,
      child: GestureDetector(
        key: rowKey,
        behavior: HitTestBehavior.opaque,
        onTap: onTap,
        child: content,
      ),
    );
  }
}
