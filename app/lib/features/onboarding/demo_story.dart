import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:vela_api_client/vela_api_client.dart';

import '../../core/theme/tokens.dart';
import '../story/replies_sheet_hook.dart';
import '../story/story_data.dart';
import '../story/story_screen.dart';
import '../story/story_settings.dart';

const demoWorkId = 'demo';
const _storyAsset = 'assets/demo/demo_story.json';
const _photoAsset = 'assets/demo/demo_photo.png';

/// The bundled sample story. Everything is an asset (original placeholder
/// text and a programmatically drawn picture; see tools/make_demo_photo.py).
final demoWorkProvider = FutureProvider<Work>((_) async {
  final raw = await rootBundle.loadString(_storyAsset);
  return Work.fromJson(jsonDecode(raw) as Map<String, dynamic>);
});

/// Plays [demoWorkProvider] through the real [StoryScreen]. The story
/// viewer reads its data through `workProvider` and its pictures through
/// `storyImageProviderFactoryProvider`, so a nested scope that overrides just
/// those two, plus the replies sheet (no thread to open), makes the sample
/// story play with no network and no change to the viewer.
class DemoStoryScreen extends StatelessWidget {
  const DemoStoryScreen({super.key});

  @override
  Widget build(BuildContext context) => ProviderScope(
    overrides: [
      workProvider(demoWorkId)
          .overrideWith((ref) => ref.watch(demoWorkProvider.future)),
      storyImageProviderFactoryProvider.overrideWithValue(
        (_) => const AssetImage(_photoAsset),
      ),
      openRepliesSheetProvider.overrideWithValue((
        context,
        workId,
        chapterIndex, {
        required readOnly,
      }) async {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(
            content: Text(
              'Replies open on real stories.',
              style: VelaText.sans13,
            ),
          ),
        );
      }),
    ],
    child: const StoryScreen(workId: demoWorkId, readOnly: true),
  );
}
