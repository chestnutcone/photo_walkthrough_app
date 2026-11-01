import 'package:test/test.dart';
import 'package:vela_api_client/vela_api_client.dart';


/// tests for RepliesApi
void main() {
  final instance = VelaApiClient().getRepliesApi();

  group(RepliesApi, () {
    // Reply to a chapter
    //
    // Body is cleaned by the shared text sanitizer (contract/text-sanitizer-vectors.json: NFC, control and invisible characters stripped, at most 4 stacked marks, spaces collapsed, at most one blank line) and must then be 1 to 1000 code points; bidirectional override characters are refused. A reply the safety scan flags is still returned to its author, with hiddenForOthers true.
    //
    //Future<ReplyEnvelope> addReply(String id, int index, AddReplyRequest addReplyRequest) async
    test('test addReply', () async {
      // TODO
    });

    // Delete a reply
    //
    //Future<Envelope> deleteReply(String id) async
    test('test deleteReply', () async {
      // TODO
    });

    // List replies on a chapter
    //
    //Future<ReplyListEnvelope> listReplies(String id, int index, { String cursor, int limit }) async
    test('test listReplies', () async {
      // TODO
    });

  });
}
