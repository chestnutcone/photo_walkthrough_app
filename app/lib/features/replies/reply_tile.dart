import 'package:flutter/material.dart';
import 'package:vela_api_client/vela_api_client.dart';

import '../../core/theme/tokens.dart';
import 'bidi.dart';

const _avatarColors = [
  Color(0xFFD39A74),
  Color(0xFF8FB8B0),
  Color(0xFFD8C08A),
  Color(0xFFC9C3B8),
];

enum ReplyAction { delete, report }

/// One reply: 28pt avatar, name, text and (outside read-only) one action.
class ReplyTile extends StatelessWidget {
  const ReplyTile({
    required this.reply,
    required this.isOwn,
    required this.readOnly,
    required this.onAction,
    this.showActions = true,
    super.key,
  });

  final Reply reply;
  final bool isOwn;
  final bool readOnly;

  /// False while the viewer's identity is unknown.
  final bool showActions;
  final void Function(ReplyAction action) onAction;

  @override
  Widget build(BuildContext context) {
    final name = reply.author.displayName;
    final initial = name.characters.isEmpty ? '?' : name.characters.first;
    final color =
        _avatarColors[reply.author.id.hashCode.abs() % _avatarColors.length];
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 10),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          ExcludeSemantics(
            child: Container(
              width: 28,
              height: 28,
              alignment: Alignment.center,
              decoration: BoxDecoration(color: color, shape: BoxShape.circle),
              child: Text(
                initial,
                style: VelaText.serif18.copyWith(
                  fontSize: 14,
                  color: VelaColors.background,
                ),
              ),
            ),
          ),
          const SizedBox(width: 10),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                Text(
                  name,
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  textDirection: textDirectionFor(context, name),
                  style: VelaText.sans13m,
                ),
                const SizedBox(height: 2),
                Text(
                  reply.body,
                  textDirection: textDirectionFor(context, reply.body),
                  style: VelaText.sans14.copyWith(color: VelaColors.secondary),
                ),
                if (isOwn && reply.hiddenForOthers == true)
                  Padding(
                    padding: const EdgeInsets.only(top: 4),
                    child: Text(
                      'Only you can see this',
                      style: VelaText.mono10.copyWith(color: VelaColors.accent),
                    ),
                  ),
              ],
            ),
          ),
          if (!readOnly && showActions) _action(),
        ],
      ),
    );
  }

  Widget _action() {
    final delete = isOwn;
    return Semantics(
      container: true,
      button: true,
      label: delete ? 'Delete reply' : 'Report reply',
      excludeSemantics: true,
      child: InkWell(
        key: ValueKey('${delete ? 'delete' : 'report'}-${reply.id}'),
        borderRadius: BorderRadius.circular(8),
        onTap: () => onAction(delete ? ReplyAction.delete : ReplyAction.report),
        child: ConstrainedBox(
          constraints: const BoxConstraints(minWidth: 44, minHeight: 44),
          child: Center(
            widthFactor: 1,
            child: Padding(
              padding: const EdgeInsets.symmetric(horizontal: 8),
              child: Text(delete ? 'Delete' : 'Report', style: VelaText.mono10),
            ),
          ),
        ),
      ),
    );
  }
}
