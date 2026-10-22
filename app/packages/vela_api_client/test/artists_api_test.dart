import 'package:test/test.dart';
import 'package:vela_api_client/vela_api_client.dart';


/// tests for ArtistsApi
void main() {
  final instance = VelaApiClient().getArtistsApi();

  group(ArtistsApi, () {
    // Follow an artist
    //
    //Future<Envelope> follow(String id) async
    test('test follow', () async {
      // TODO
    });

    // Get an artist by handle
    //
    //Future<ProfileEnvelope> getArtist(String handle) async
    test('test getArtist', () async {
      // TODO
    });

    // List an artist's published works
    //
    //Future<WorkListEnvelope> listArtistWorks(String handle, { String cursor, int limit }) async
    test('test listArtistWorks', () async {
      // TODO
    });

    // List an artist's followers
    //
    //Future<ProfileListEnvelope> listFollowers(String id, { String cursor, int limit }) async
    test('test listFollowers', () async {
      // TODO
    });

    // Send an inquiry to an artist
    //
    //Future<InquiryEnvelope> sendInquiry(String id, SendInquiryRequest sendInquiryRequest) async
    test('test sendInquiry', () async {
      // TODO
    });

    // Suggested artists to follow
    //
    //Future<ProfileListEnvelope> suggestedArtists({ String cursor, int limit }) async
    test('test suggestedArtists', () async {
      // TODO
    });

    // Unfollow an artist
    //
    //Future<Envelope> unfollow(String id) async
    test('test unfollow', () async {
      // TODO
    });

  });
}
