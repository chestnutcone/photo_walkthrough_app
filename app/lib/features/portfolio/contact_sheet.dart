import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:vela_api_client/vela_api_client.dart';

import '../../core/api/api_exception.dart';
import '../../core/api/api_providers.dart';
import '../../core/theme/tokens.dart';
import '../story/story_settings.dart';
import 'contact_rules.dart';
import 'portfolio_format.dart';

/// Who the inquiry goes to and what it is about.
class InquiryTarget {
  const InquiryTarget({
    required this.artistId,
    required this.artistName,
    this.replyTime,
    this.workId,
    this.workTitle,
    this.workThumbUrl,
    this.initialType,
  });

  final String artistId;
  final String artistName;

  /// "2 days": the artist's usual reply time, when published.
  final String? replyTime;
  final String? workId;
  final String? workTitle;
  final String? workThumbUrl;

  /// One of the five type labels, e.g. "Buy a print".
  final String? initialType;
}

/// Opens the contact sheet. Callers make sure the viewer is signed in first
/// (the inquiry endpoint needs an account).
Future<void> showContactSheet(BuildContext context, InquiryTarget target) =>
    showModalBottomSheet<void>(
      context: context,
      isScrollControlled: true,
      useSafeArea: true,
      backgroundColor: VelaColors.sheet,
      barrierColor: const Color(0x66000000),
      shape: const RoundedRectangleBorder(
        borderRadius: BorderRadius.vertical(top: Radius.circular(26)),
      ),
      builder: (_) => ContactSheet(target: target),
    );

class ContactSheet extends ConsumerStatefulWidget {
  const ContactSheet({super.key, required this.target});

  final InquiryTarget target;

  @override
  ConsumerState<ContactSheet> createState() => _ContactSheetState();
}

class _ContactSheetState extends ConsumerState<ContactSheet> {
  final _email = TextEditingController();
  final _message = TextEditingController();
  late SendInquiryRequestTypeEnum _type;
  bool _sending = false;
  ApiException? _error;
  ({SendInquiryRequestTypeEnum type, String email})? _sent;

  @override
  void initState() {
    super.initState();
    _type = inquiryTypeFor(widget.target.initialType);
    _email.addListener(_changed);
    _message.addListener(_changed);
  }

  @override
  void dispose() {
    _email.dispose();
    _message.dispose();
    super.dispose();
  }

  void _changed() => setState(() {});

  bool get _canSend =>
      !_sending && canSendInquiry(email: _email.text, message: _message.text);

  Future<void> _send() async {
    if (!_canSend) return;
    final t = widget.target;
    final email = _email.text.trim();
    final type = _type;
    final request = SendInquiryRequest(
      type: type,
      email: email,
      message: cleanInquiryMessage(_message.text),
      workId: t.workId,
    );
    setState(() {
      _sending = true;
      _error = null;
    });
    try {
      final env = (await apiGuard(
        () => ref
            .read(apiProvider)
            .getArtistsApi()
            .sendInquiry(id: t.artistId, sendInquiryRequest: request),
      )).data;
      unwrapEnvelope(
        success: env?.success ?? false,
        data: env?.data,
        error: env?.error,
      );
      if (!mounted) return;
      setState(() {
        _sending = false;
        _sent = (type: type, email: email);
      });
    } on ApiException catch (e) {
      if (!mounted) return;
      setState(() {
        _sending = false;
        _error = e;
      });
    }
  }

  @override
  Widget build(BuildContext context) {
    final inset = MediaQuery.viewInsetsOf(context).bottom;
    return Padding(
      padding: EdgeInsets.only(bottom: inset),
      child: SingleChildScrollView(
        child: Container(
          key: const ValueKey('contact-sheet'),
          padding: const EdgeInsets.fromLTRB(20, 10, 20, 24),
          child: _sent != null ? _success(_sent!) : _form(),
        ),
      ),
    );
  }

  Widget _grabber() => Center(
    child: Container(
      width: 36,
      height: 4,
      margin: const EdgeInsets.only(bottom: 16),
      decoration: BoxDecoration(
        color: VelaColors.faint,
        borderRadius: BorderRadius.circular(2),
      ),
    ),
  );

  Widget _success(({SendInquiryRequestTypeEnum type, String email}) sent) {
    final t = widget.target;
    final first = firstName(t.artistName);
    final reply = (t.replyTime?.trim().isNotEmpty ?? false)
        ? ' Usually replies within ${t.replyTime!.trim()}.'
        : '';
    return Semantics(
      liveRegion: true,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.center,
        children: [
          _grabber(),
          const SizedBox(height: 12),
          Container(
            width: 56,
            height: 56,
            decoration: const BoxDecoration(
              color: VelaColors.accent,
              shape: BoxShape.circle,
            ),
            child: const Icon(Icons.check, color: VelaColors.background),
          ),
          const SizedBox(height: 16),
          Text('Sent to $first.', style: VelaText.serif32),
          const SizedBox(height: 10),
          Text(
            "Your ${inquirySummaryNoun(sent.type)} is in $first's inbox."
            '$reply Replies go to ${sent.email}.',
            textAlign: TextAlign.center,
            style: VelaText.sans14.copyWith(color: VelaColors.muted),
          ),
          const SizedBox(height: 24),
          FilledButton(
            onPressed: () => Navigator.of(context).pop(),
            style: _filled(),
            child: const Text('Back to portfolio'),
          ),
        ],
      ),
    );
  }

  ButtonStyle _filled() => FilledButton.styleFrom(
    minimumSize: const Size.fromHeight(48),
    backgroundColor: VelaColors.ink,
    foregroundColor: VelaColors.background,
    disabledBackgroundColor: VelaColors.input,
    disabledForegroundColor: VelaColors.faint,
    textStyle: VelaText.sans14m,
    shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(12)),
  );

  Widget _form() {
    final t = widget.target;
    final first = firstName(t.artistName);
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        _grabber(),
        Semantics(
          header: true,
          child: Text('WHAT IS THIS ABOUT', style: VelaText.monoKicker),
        ),
        const SizedBox(height: 10),
        Wrap(
          spacing: 8,
          runSpacing: 8,
          children: [
            for (final (label, type) in inquiryTypes)
              _TypeChip(
                label: label,
                selected: type == _type,
                onTap: _sending ? null : () => setState(() => _type = type),
              ),
          ],
        ),
        if (t.workId != null && (t.workTitle?.isNotEmpty ?? false)) ...[
          const SizedBox(height: 14),
          _WorkChip(title: t.workTitle!, thumbUrl: t.workThumbUrl),
        ],
        const SizedBox(height: 16),
        TextField(
          key: const ValueKey('contact-email'),
          controller: _email,
          enabled: !_sending,
          keyboardType: TextInputType.emailAddress,
          textInputAction: TextInputAction.next,
          autocorrect: false,
          enableSuggestions: false,
          inputFormatters: [
            LengthLimitingTextInputFormatter(kInquiryMaxEmail),
            FilteringTextInputFormatter.deny(RegExp(r'\s')),
          ],
          style: VelaText.sans14,
          decoration: _field('Your email (replies go here)'),
        ),
        const SizedBox(height: 10),
        TextField(
          key: const ValueKey('contact-message'),
          controller: _message,
          enabled: !_sending,
          minLines: 4,
          maxLines: 8,
          keyboardType: TextInputType.multiline,
          inputFormatters: [
            LengthLimitingTextInputFormatter(kInquiryMaxMessage),
          ],
          style: VelaText.sans14,
          decoration: _field(messageHint(_type)),
        ),
        const SizedBox(height: 10),
        Text(
          (t.replyTime?.trim().isNotEmpty ?? false)
              ? '$first usually replies within ${t.replyTime!.trim()}.'
              : 'Your email is shared with $first so they can reply.',
          style: VelaText.sans12.copyWith(color: VelaColors.muted),
        ),
        if (_error != null) ...[
          const SizedBox(height: 10),
          Semantics(
            liveRegion: true,
            child: Text(
              inquiryErrorText(_error!),
              key: const ValueKey('contact-error'),
              style: VelaText.sans13.copyWith(color: const Color(0xFFE5806B)),
            ),
          ),
        ],
        const SizedBox(height: 16),
        FilledButton(
          key: const ValueKey('contact-send'),
          onPressed: _canSend ? _send : null,
          style: _filled(),
          child: Text(_sending ? 'Sending…' : 'Send inquiry'),
        ),
      ],
    );
  }

  InputDecoration _field(String hint) => InputDecoration(
    hintText: hint,
    hintStyle: VelaText.sans14.copyWith(color: VelaColors.faint),
    border: OutlineInputBorder(
      borderRadius: BorderRadius.circular(12),
      borderSide: BorderSide.none,
    ),
    contentPadding: const EdgeInsets.symmetric(horizontal: 14, vertical: 14),
  );
}

class _TypeChip extends StatelessWidget {
  const _TypeChip({
    required this.label,
    required this.selected,
    required this.onTap,
  });

  final String label;
  final bool selected;
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) => Semantics(
    button: true,
    selected: selected,
    label: label,
    excludeSemantics: true,
    onTap: onTap,
    child: GestureDetector(
      key: ValueKey('contact-chip-$label'),
      behavior: HitTestBehavior.opaque,
      onTap: onTap,
      child: ConstrainedBox(
        constraints: const BoxConstraints(minHeight: 44),
        child: Center(
          widthFactor: 1,
          child: Container(
            padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 8),
            decoration: BoxDecoration(
              color: selected ? VelaColors.ink : Colors.transparent,
              borderRadius: BorderRadius.circular(99),
              border: Border.all(
                color: selected ? VelaColors.ink : VelaColors.outline,
              ),
            ),
            child: Text(
              label,
              style: VelaText.sans13m.copyWith(
                color: selected ? VelaColors.background : VelaColors.ink,
              ),
            ),
          ),
        ),
      ),
    ),
  );
}

class _WorkChip extends ConsumerWidget {
  const _WorkChip({required this.title, required this.thumbUrl});

  final String title;
  final String? thumbUrl;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final url = thumbUrl;
    return Semantics(
      label: 'About the work: $title',
      excludeSemantics: true,
      child: Container(
        padding: const EdgeInsets.all(8),
        decoration: BoxDecoration(
          color: VelaColors.input,
          borderRadius: BorderRadius.circular(12),
        ),
        child: Row(
          children: [
            ClipRRect(
              borderRadius: BorderRadius.circular(8),
              child: SizedBox(
                width: 40,
                height: 40,
                child: url == null
                    ? const ColoredBox(color: VelaColors.sheet)
                    : Image(
                        image: ref.watch(storyImageProviderFactoryProvider)(
                          url,
                        ),
                        fit: BoxFit.cover,
                        errorBuilder: (_, _, _) =>
                            const ColoredBox(color: VelaColors.sheet),
                      ),
              ),
            ),
            const SizedBox(width: 10),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text('About the work', style: VelaText.mono10),
                  Text(
                    title,
                    style: VelaText.sans13m,
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                  ),
                ],
              ),
            ),
          ],
        ),
      ),
    );
  }
}
