import 'package:test/test.dart';
import 'package:vela_api_client/vela_api_client.dart';


/// tests for NotificationsApi
void main() {
  final instance = VelaApiClient().getNotificationsApi();

  group(NotificationsApi, () {
    // List notifications
    //
    //Future<NotificationListEnvelope> listNotifications({ String cursor, int limit }) async
    test('test listNotifications', () async {
      // TODO
    });

    // Mark notifications read
    //
    //Future<Envelope> markRead(MarkReadRequest markReadRequest) async
    test('test markRead', () async {
      // TODO
    });

    // Count unread notifications
    //
    //Future<UnreadCountEnvelope> unreadCount() async
    test('test unreadCount', () async {
      // TODO
    });

  });
}
