import 'dart:convert';

import 'package:flutter/foundation.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';

@immutable
class Session {
  const Session({
    required this.accessToken,
    required this.refreshToken,
    required this.accessExpiresAt,
  });

  final String accessToken;
  final String refreshToken;
  final DateTime accessExpiresAt;

  String encode() => jsonEncode({
    'accessToken': accessToken,
    'refreshToken': refreshToken,
    'accessExpiresAt': accessExpiresAt.toUtc().toIso8601String(),
  });

  /// Throws [FormatException] or [TypeError] on malformed input.
  factory Session.decode(String json) {
    final map = jsonDecode(json) as Map<String, dynamic>;
    return Session(
      accessToken: map['accessToken'] as String,
      refreshToken: map['refreshToken'] as String,
      accessExpiresAt: DateTime.parse(map['accessExpiresAt'] as String),
    );
  }

  @override
  bool operator ==(Object other) =>
      other is Session &&
      other.accessToken == accessToken &&
      other.refreshToken == refreshToken &&
      other.accessExpiresAt == accessExpiresAt;

  @override
  int get hashCode => Object.hash(accessToken, refreshToken, accessExpiresAt);

  /// Never print tokens.
  @override
  String toString() => 'Session(expiresAt: $accessExpiresAt)';
}

/// Persistence of the serialized session. Injectable for tests.
abstract interface class SessionStorage {
  Future<String?> read();
  Future<void> write(String json);
  Future<void> delete();
}

class SecureSessionStorage implements SessionStorage {
  SecureSessionStorage([FlutterSecureStorage? storage])
    : _storage = storage ?? const FlutterSecureStorage();

  static const _key = 'vela.session';
  final FlutterSecureStorage _storage;

  @override
  Future<String?> read() => _storage.read(key: _key);
  @override
  Future<void> write(String json) => _storage.write(key: _key, value: json);
  @override
  Future<void> delete() => _storage.delete(key: _key);
}

final sessionStorageProvider = Provider<SessionStorage>(
  (ref) => SecureSessionStorage(),
);
