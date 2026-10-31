import 'dart:typed_data';

import 'package:image/image.dart' as img;

/// Sample data for the screenshot gallery, loosely after the handoff
/// prototype (`design_handoff_vela_app/Vela App.dc.html`).

Map<String, Object?> profile(
  String id, {
  required String handle,
  required String name,
  String city = 'Lisbon',
  String discipline = 'Photographer',
  String bio = '',
  bool? isFollowing = false,
  int followers = 2412,
  int following = 38,
  int works = 3,
  int chapters = 11,
  bool full = false,
}) => {
  'id': id,
  'handle': handle,
  'displayName': name,
  'bio': bio,
  'followerCount': followers,
  'followingCount': following,
  'workCount': works,
  'chapterCount': chapters,
  'isFollowing': ?isFollowing,
  'onboardingComplete': true,
  'city': city,
  'discipline': discipline,
  if (full) ...{
    'availability': 'Open for commissions',
    'website': 'https://ines.example',
    'social': '@ines.alvarez',
    'studio': 'Rua das Flores 12, Lisbon',
    'replyTime': '2 days',
    'publicEmail': 'studio@ines.example',
    'services': [
      {
        'title': 'Editorial commissions',
        'description': 'Coastal and travel stories, two to five days.',
        'price': 'from \$1,800',
      },
      {
        'title': 'Fine art prints',
        'description': 'Archival pigment, signed, editions of 25.',
        'price': 'from \$240',
      },
    ],
    'shows': [
      {'year': '2026', 'text': 'Tide Lines, Galeria Zé dos Bois, Lisbon'},
      {'year': '2025', 'text': 'Feature in Aperture, "The Patient Eye"'},
    ],
  },
};

final ines = profile(
  'a1',
  handle: 'ines',
  name: 'Ines Alvarez',
  bio:
      'I photograph harbours at the hour nobody is working. Every picture '
      'has a story; here are mine, one detail at a time.',
  full: true,
);
final teo = profile(
  'a2',
  handle: 'teo',
  name: 'Teo Marchetti',
  city: 'Turin',
  discipline: 'Painter',
  followers: 812,
);
final mara = profile(
  'a3',
  handle: 'mara',
  name: 'Mara Okafor',
  city: 'Lagos',
  discipline: 'Photographer',
  followers: 5120,
  isFollowing: true,
);
final meProfile = profile(
  'a9',
  handle: 'you',
  name: 'Sam Reyes',
  city: 'Austin',
  bio: 'Weekend photographer.',
  isFollowing: null,
  followers: 3,
  following: 2,
  works: 0,
  chapters: 0,
);

Map<String, Object?> summary(Map<String, Object?> p) => {
  'id': p['id'],
  'handle': p['handle'],
  'displayName': p['displayName'],
  'isFollowing': ?p['isFollowing'],
};

Map<String, Object?> chapter(
  double x,
  double y,
  String title,
  String body, {
  String? meta,
  int replies = 0,
  double? zoom = 2.4,
}) => {
  'x': x,
  'y': y,
  'zoom': zoom,
  'title': title,
  'body': body,
  'meta': ?meta,
  'replyCount': replies,
};

Map<String, Object?> work(
  String id,
  Map<String, Object?> author, {
  required String title,
  required String intro,
  required List<Map<String, Object?>> chapters,
  bool isOwner = false,
  String date = '2026-10-02T10:00:00Z',
}) => {
  'id': id,
  'author': summary(author),
  'title': title,
  'intro': intro,
  'altText': 'Photograph: $title',
  'isOwner': isOwner,
  'imageUrl': 'https://img.test/$id-full.jpg',
  'thumbUrl': 'https://img.test/$id-thumb.jpg',
  'feedUrl': 'https://img.test/$id-feed.jpg',
  'imageWidth': 3000,
  'imageHeight': 4000,
  'chapters': chapters,
  'createdAt': date,
  'publishedAt': date,
  'shareUrl': 'https://vela.example/s/$id',
};

final lowTide = work(
  'w1',
  ines,
  title: 'Low Tide, Second Light',
  intro:
      'A ferry, a tide line, and forty minutes of waiting. Walk through what '
      'I was looking at.',
  chapters: [
    chapter(
      0.32,
      0.38,
      'The ferry, leaving',
      'I waited for it to clear the breakwater so the wake would draw a '
          'line across the bay.',
      meta: '85mm · f/4 · 1/500',
    ),
    chapter(
      0.62,
      0.55,
      'Tide line',
      'The water had pulled back maybe ten metres. You can read the morning '
          'in the wet sand.',
      meta: '85mm · f/5.6 · 1/250',
      replies: 2,
    ),
    chapter(
      0.45,
      0.74,
      'Rope on the slipway',
      'Someone left it coiled exactly like this. I did not touch it.',
      replies: 1,
    ),
    chapter(
      0.78,
      0.24,
      'Second light',
      'The sun came through a gap in the cloud for about a minute. This is '
          'that minute.',
      meta: '35mm · f/8 · 1/1000',
    ),
  ],
);

final fieldNotes = work(
  'w2',
  mara,
  title: 'Field Notes No. 4',
  intro:
      'A dry lake bed, mapped one detail at a time. Noon light, which '
      'everyone tells you to avoid.',
  date: '2026-09-18T10:00:00Z',
  chapters: [
    chapter(0.5, 0.4, 'Cracks', 'Every plate is a little map.'),
    chapter(0.3, 0.7, 'Footprints', 'A heron, not me.', replies: 4),
    chapter(0.7, 0.6, 'Salt', 'It squeaks underfoot.'),
  ],
);

final overpass = work(
  'w3',
  teo,
  title: 'Overpass',
  intro: 'Two kinds of streetlight meeting under one bridge.',
  date: '2026-08-30T10:00:00Z',
  chapters: [
    chapter(0.4, 0.3, 'Sodium', 'The orange one is older.'),
    chapter(0.6, 0.6, 'LED', 'The new ones are blue-white.'),
  ],
);

final ropes = work(
  'w4',
  ines,
  title: 'Ropes',
  intro: 'Mooring lines photographed from close up, like a portrait.',
  date: '2026-07-11T10:00:00Z',
  chapters: [chapter(0.5, 0.5, 'Fray', 'Salt does this in a season.')],
);

final saguaro = work(
  'w5',
  ines,
  title: 'Saguaro, Backlit',
  intro: 'One cactus, one sun, one try.',
  date: '2026-06-02T10:00:00Z',
  chapters: [
    chapter(0.5, 0.3, 'Rim light', 'Spines catch the sun.'),
    chapter(0.4, 0.8, 'Shadow', 'Longer than the cactus is tall.'),
  ],
);

final feedWorks = [lowTide, fieldNotes, overpass];
final inesWorks = [lowTide, ropes, saguaro];

Map<String, Object?> reply(
  String id,
  Map<String, Object?> author,
  String body, {
  String at = '2026-10-03T09:00:00Z',
}) => {
  'id': id,
  'workId': 'w1',
  'chapterIndex': 1,
  'author': author,
  'body': body,
  'createdAt': at,
};

final tideReplies = [
  reply('r1', teo, 'The texture in the sand is unreal. What time was this?'),
  reply(
    'r2',
    mara,
    'I love that you waited for the ferry. Patience shows.',
    at: '2026-10-03T11:30:00Z',
  ),
];

Map<String, Object?> notif(
  String id,
  String type,
  Map<String, Object?> actor, {
  String? workId = 'w1',
  String? title = 'Low Tide, Second Light',
  int? chapter = 1,
  bool read = false,
  int count = 1,
  String at = '2026-10-30T10:00:00Z',
}) => {
  'id': id,
  'type': type,
  'actor': actor,
  'workId': workId,
  'workTitle': title,
  'chapterIndex': chapter,
  'createdAt': at,
  'read': read,
  'count': count,
  'ids': [id],
};

final notifications = [
  notif('n1', 'reply', teo, count: 3),
  notif('n2', 'follow', mara, workId: null, title: null, chapter: null),
  notif(
    'n3',
    'new_story',
    ines,
    workId: 'w4',
    title: 'Ropes',
    chapter: null,
    read: true,
    at: '2026-10-28T10:00:00Z',
  ),
  notif(
    'n4',
    'inquiry',
    mara,
    workId: null,
    title: null,
    chapter: null,
    read: true,
    at: '2026-10-21T10:00:00Z',
  ),
];

/// Deterministic "photograph": a dusk gradient with a horizon, a sun and a
/// few dark shapes. Different seeds give different palettes.
Uint8List samplePhoto(int seed, {int w = 360, int h = 480}) {
  const palettes = [
    [(40, 52, 74), (214, 154, 116), (28, 36, 48)],
    [(196, 170, 120), (232, 214, 170), (120, 96, 66)],
    [(22, 26, 40), (220, 140, 60), (18, 18, 24)],
    [(70, 90, 96), (180, 196, 186), (40, 52, 54)],
    [(120, 60, 40), (240, 190, 110), (60, 40, 30)],
  ];
  final p = palettes[seed % palettes.length];
  final image = img.Image(width: w, height: h);
  final horizon = (h * (0.5 + 0.05 * (seed % 3))).round();
  for (var y = 0; y < h; y++) {
    final top = y < horizon;
    final t = top ? y / horizon : (y - horizon) / (h - horizon);
    final a = top ? p[0] : p[1];
    final b = top ? p[1] : p[2];
    final c = img.ColorRgb8(
      (a.$1 + (b.$1 - a.$1) * t).round(),
      (a.$2 + (b.$2 - a.$2) * t).round(),
      (a.$3 + (b.$3 - a.$3) * t).round(),
    );
    img.drawLine(image, x1: 0, y1: y, x2: w - 1, y2: y, color: c);
  }
  img.fillCircle(
    image,
    x: (w * (0.3 + 0.1 * (seed % 4))).round(),
    y: horizon - 30,
    radius: 22,
    color: img.ColorRgb8(250, 226, 180),
  );
  for (var i = 0; i < 4; i++) {
    final x = (w * (0.1 + 0.22 * i + 0.03 * seed)).round() % w;
    img.fillRect(
      image,
      x1: x,
      y1: horizon - 8 - 6 * i,
      x2: x + 18 + 4 * i,
      y2: horizon + 4,
      color: img.ColorRgb8(p[2].$1, p[2].$2, p[2].$3),
    );
  }
  return Uint8List.fromList(img.encodePng(image, level: 6));
}
