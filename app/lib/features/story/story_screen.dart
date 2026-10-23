import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:vela_api_client/vela_api_client.dart';

import '../../core/api/api_exception.dart';
import '../../core/theme/tokens.dart';
import 'camera_tween.dart';
import 'follow_action.dart';
import 'inquiry_sheet_hook.dart';
import 'motion.dart';
import 'photo_layout.dart';
import 'replies_sheet_hook.dart';
import 'story_controller.dart';
import 'story_data.dart';
import 'story_settings.dart';
import 'camera_math.dart';
import 'widgets/bottom_card.dart';
import 'widgets/pin_layer.dart';
import 'widgets/spotlight_ring.dart';
import 'widgets/story_photo.dart';
import 'widgets/story_top_bar.dart';

/// Tap zones for walking through chapters: y 110..510, left 30% / right 70%.
const double _zoneTop = 110;
const double _zoneHeight = 400;
const double _zoneSplit = 0.3;

/// The story viewer. [readOnly] (the web viewer) hides actions that need a
/// signed-in account: follow, print inquiry.
class StoryScreen extends ConsumerWidget {
  const StoryScreen({super.key, required this.workId, this.readOnly = false});

  final String workId;
  final bool readOnly;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final work = ref.watch(workProvider(workId));
    return work.when(
      data: (w) => _StoryView(key: ValueKey(w.id), work: w, readOnly: readOnly),
      loading: () => const _StatusScaffold(
        child: CircularProgressIndicator(color: VelaColors.accent),
      ),
      error: (e, _) => _StatusScaffold(
        child: isStoryUnavailable(e)
            ? const _Message("This story isn't available")
            : _Message(
                "Couldn't load this story",
                retry: () => ref.invalidate(workProvider(workId)),
              ),
      ),
    );
  }
}

class _StatusScaffold extends StatelessWidget {
  const _StatusScaffold({required this.child});

  final Widget child;

  @override
  Widget build(BuildContext context) => Scaffold(
    backgroundColor: Colors.black,
    body: Stack(
      children: [
        Center(child: child),
        SafeArea(
          child: Align(
            alignment: Alignment.topRight,
            child: IconButton(
              key: const ValueKey('close'),
              tooltip: 'Close',
              icon: const Icon(Icons.close, color: VelaColors.ink),
              onPressed: () => _leave(context),
            ),
          ),
        ),
      ],
    ),
  );
}

class _Message extends StatelessWidget {
  const _Message(this.text, {this.retry});

  final String text;
  final VoidCallback? retry;

  @override
  Widget build(BuildContext context) => Column(
    mainAxisSize: MainAxisSize.min,
    children: [
      Text(text, style: VelaText.serif24),
      if (retry != null)
        TextButton(
          onPressed: retry,
          child: Text('Retry', style: VelaText.sans13m),
        ),
    ],
  );
}

void _leave(BuildContext context, {String fallback = '/'}) {
  if (context.canPop()) {
    context.pop();
  } else {
    context.go(fallback);
  }
}

class _StoryView extends ConsumerStatefulWidget {
  const _StoryView({super.key, required this.work, required this.readOnly});

  final Work work;
  final bool readOnly;

  @override
  ConsumerState<_StoryView> createState() => _StoryViewState();
}

class _StoryViewState extends ConsumerState<_StoryView> {
  late bool _following = widget.work.author.isFollowing ?? false;
  bool _followBusy = false;
  bool _photoFailed = false;
  int _photoAttempt = 0;

  /// The ring keeps its last position while it fades out on intro / end.
  Offset _lastSpotlight = Offset.zero;

  final _cardKey = GlobalKey();
  double _cardTop = double.infinity;

  Work get _work => widget.work;
  StoryKey get _key =>
      StoryKey(workId: _work.id, chapterCount: _work.chapters.length);
  StoryController get _ctl => ref.read(storyControllerProvider(_key).notifier);

  @override
  Widget build(BuildContext context) {
    final state = ref.watch(storyControllerProvider(_key));
    final multiplier = ref.watch(zoomMultiplierProvider);
    final media = MediaQuery.of(context);
    final viewport = media.size;
    final pins = [for (final c in _work.chapters) Offset(c.x, c.y)];
    final frame = computePhotoFrame(
      viewport: viewport,
      aspect: _aspect(),
      pins: pins,
    );
    final chapterIdx = state.chapterIndex(_work.chapters.length);
    final camera = chapterIdx < 0
        ? CameraState(
            scale: 1,
            tx: 0,
            ty: 0,
            spotlight: _spotlightFallback(viewport),
          )
        : _cameraFor(_work.chapters[chapterIdx], frame, multiplier, viewport);
    _lastSpotlight = camera.spotlight;
    final pinsVisible = state.mode == StoryMode.explore || state.step == 0;

    WidgetsBinding.instance.addPostFrameCallback((_) => _measureCard());
    return Scaffold(
      backgroundColor: Colors.black,
      body: Stack(
        clipBehavior: Clip.hardEdge,
        children: [
          Positioned.fill(
            child: TweenAnimationBuilder<CameraState>(
              tween: CameraTween(end: camera),
              duration: motionDuration(context, cameraDuration),
              curve: cameraCurve,
              builder: (context, cam, _) => Stack(
                clipBehavior: Clip.hardEdge,
                children: [
                  _photoLayer(cam, viewport, frame, state, pinsVisible),
                  Positioned.fill(
                    child: SpotlightRing(
                      center: cam.spotlight,
                      visible: chapterIdx >= 0,
                    ),
                  ),
                ],
              ),
            ),
          ),
          _topGradient(),
          if (state.mode == StoryMode.story &&
              state.step >= 1 &&
              state.step <= _work.chapters.length)
            ..._tapZones(viewport.width),
          // Above the tap zones so Retry stays tappable during chapters.
          if (_photoFailed) _photoError(),
          _topBar(state, media),
          StoryBottomCard(
            work: _work,
            state: state,
            endActions: _endActions(),
            onBegin: _ctl.next,
            onZoomOut: _ctl.zoomOut,
            onReplies: _toggleReplies,
            cardKey: _cardKey,
          ),
        ],
      ),
    );
  }

  /// Top of the bottom card, so pins can stay clear of it.
  void _measureCard() {
    final box = _cardKey.currentContext?.findRenderObject() as RenderBox?;
    if (!mounted || box == null || !box.hasSize) return;
    final top = box.localToGlobal(Offset.zero).dy;
    if ((top - _cardTop).abs() > 0.5) setState(() => _cardTop = top);
  }

  double? _aspect() {
    final w = _work.imageWidth;
    final h = _work.imageHeight;
    return (w != null && h != null && w > 0 && h > 0) ? w / h : null;
  }

  Offset _spotlightFallback(Size v) => _lastSpotlight == Offset.zero
      ? Offset(v.width / 2, v.height * 0.34)
      : _lastSpotlight;

  CameraState _cameraFor(
    Chapter c,
    PhotoFrame frame,
    double multiplier,
    Size viewport,
  ) {
    final at = frame.toLayer(Offset(c.x, c.y));
    return computeCamera(
      x: at.dx,
      y: at.dy,
      zoom: c.zoom,
      zoomMultiplier: multiplier,
      viewport: viewport,
    );
  }

  Widget _photoLayer(
    CameraState cam,
    Size viewport,
    PhotoFrame frame,
    StoryState state,
    bool pinsVisible,
  ) {
    final matrix = Matrix4.identity()
      ..translateByDouble(cam.tx, cam.ty, 0, 1)
      ..scaleByDouble(cam.scale, cam.scale, 1, 1);
    return Positioned(
      left: 0,
      top: 0,
      width: viewport.width,
      height: viewport.height,
      child: Transform(
        key: const ValueKey('photo-layer'),
        alignment: Alignment.topLeft,
        transform: matrix,
        child: GestureDetector(
          behavior: HitTestBehavior.opaque,
          onTap: () {
            if (state.mode == StoryMode.explore && state.exploreSel >= 0) {
              _ctl.zoomOut();
            }
          },
          child: Stack(
            clipBehavior: Clip.hardEdge,
            children: [
              Positioned.fill(
                child: Semantics(
                  image: true,
                  label: _work.altText?.isNotEmpty == true
                      ? _work.altText
                      : 'Photograph: ${_work.title}',
                  child: Stack(
                    clipBehavior: Clip.hardEdge,
                    children: [
                      StoryPhoto(
                        rect: frame.rect,
                        imageUrl: _work.imageUrl,
                        thumbUrl: _work.thumbUrl ?? _work.feedUrl,
                        attempt: _photoAttempt,
                        onError: _onPhotoError,
                      ),
                    ],
                  ),
                ),
              ),
              Positioned.fill(
                child: PinLayer(
                  pins: [
                    for (var i = 0; i < _work.chapters.length; i++)
                      PinSpec(
                        layerFraction: frame.toLayer(
                          Offset(_work.chapters[i].x, _work.chapters[i].y),
                        ),
                        label: 'Chapter ${i + 1}: ${_work.chapters[i].title}',
                      ),
                  ],
                  viewport: viewport,
                  camera: cam,
                  safeBottom: _cardTop,
                  visible: pinsVisible,
                  onTap: _ctl.selectPin,
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }

  void _onPhotoError() {
    if (mounted && !_photoFailed) setState(() => _photoFailed = true);
  }

  Widget _photoError() => Positioned.fill(
    child: Center(
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          Text("Couldn't load this photo", style: VelaText.sans14),
          TextButton(
            onPressed: _retryPhoto,
            child: Text('Retry', style: VelaText.sans13m),
          ),
        ],
      ),
    ),
  );

  void _retryPhoto() {
    final factory = ref.read(storyImageProviderFactoryProvider);
    final url = _work.imageUrl ?? _work.thumbUrl;
    if (url != null) factory(url).evict();
    setState(() {
      _photoFailed = false;
      _photoAttempt++;
    });
  }

  Widget _topGradient() => const Positioned(
    left: 0,
    right: 0,
    top: 0,
    height: 170,
    child: IgnorePointer(
      child: DecoratedBox(
        decoration: BoxDecoration(
          gradient: LinearGradient(
            begin: Alignment.topCenter,
            end: Alignment.bottomCenter,
            colors: [Color(0x8C000000), Color(0x00000000)],
          ),
        ),
      ),
    ),
  );

  List<Widget> _tapZones(double width) => [
    Positioned(
      left: 0,
      top: _zoneTop,
      width: width * _zoneSplit,
      height: _zoneHeight,
      child: GestureDetector(
        behavior: HitTestBehavior.opaque,
        onTap: _ctl.prev,
      ),
    ),
    Positioned(
      right: 0,
      top: _zoneTop,
      width: width * (1 - _zoneSplit),
      height: _zoneHeight,
      child: GestureDetector(
        behavior: HitTestBehavior.opaque,
        onTap: _ctl.next,
      ),
    ),
  ];

  Widget _topBar(StoryState state, MediaQueryData media) {
    final top = media.padding.top + 2 > 52 ? media.padding.top + 2 : 52.0;
    return Positioned(
      left: 12,
      right: 12,
      top: top,
      child: StoryTopBar(
        work: _work,
        mode: state.mode,
        step: state.step,
        onJump: _ctl.jump,
        onMode: _ctl.setMode,
        onClose: _close,
        onArtist: () => context.push('/artist/${_work.author.handle}'),
      ),
    );
  }

  void _close() {
    final fallback = _work.isOwner ? '/artist/${_work.author.handle}' : '/';
    _leave(context, fallback: fallback);
  }

  Future<void> _toggleReplies(int chapterIndex) async {
    final ctl = _ctl;
    if (ref.read(storyControllerProvider(_key)).threadOpen) {
      ctl.setThreadOpen(false);
      return;
    }
    ctl.setThreadOpen(true);
    try {
      await ref.read(openRepliesSheetProvider)(
        context,
        _work.id,
        chapterIndex,
        readOnly: widget.readOnly,
      );
    } on Object catch (e) {
      // Type only: never log user text.
      debugPrint('replies sheet failed: ${e.runtimeType}');
    } finally {
      if (mounted) ctl.setThreadOpen(false);
    }
  }

  String get _first => _work.author.displayName.split(' ').first;

  List<EndAction> _endActions() {
    final owner = _work.isOwner;
    return [
      EndAction(
        label: 'Explore freely',
        onTap: () => _ctl.setMode(StoryMode.explore),
      ),
      EndAction(label: 'Replay', onTap: () => _ctl.setMode(StoryMode.story)),
      if (owner)
        EndAction(
          label: 'Your portfolio',
          onTap: () => context.push('/artist/${_work.author.handle}'),
        )
      else if (!widget.readOnly)
        EndAction(
          label: _following ? 'Following $_first' : 'Follow $_first',
          onTap: _toggleFollow,
        ),
      // The API offers no "next story" hint, so this goes back to the feed.
      EndAction(
        label: 'Next story →',
        filled: true,
        onTap: () => context.go('/'),
      ),
      if (!owner && !widget.readOnly)
        EndAction(
          label: 'Ask about a print of this work',
          fullWidth: true,
          onTap: () => ref.read(openInquirySheetProvider)(
            context,
            artistId: _work.author.id,
            workId: _work.id,
            type: 'Buy a print',
          ),
        ),
    ];
  }

  Future<void> _toggleFollow() async {
    if (_followBusy) return;
    _followBusy = true;
    final messenger = ScaffoldMessenger.of(context);
    try {
      final result = await toggleArtistFollow(
        context: context,
        ref: ref,
        artistId: _work.author.id,
        currentlyFollowing: _following,
      );
      if (mounted && result != null) setState(() => _following = result);
    } on ApiException {
      messenger.showSnackBar(
        const SnackBar(content: Text("Couldn't update follow. Try again.")),
      );
    } finally {
      _followBusy = false;
    }
  }
}
