import 'package:test/test.dart';
import 'package:vela_api_client/vela_api_client.dart';


/// tests for LegalApi
void main() {
  final instance = VelaApiClient().getLegalApi();

  group(LegalApi, () {
    // Send a counter-notice for a story removed after a copyright notice
    //
    // Owner only, for a story that is currently removed after a takedown notice. Any other story (not yours, not removed, no takedown notice, unknown id) answers the same 404 work_not_found. One counter-notice per story (409 counter_notice_exists). Free text is stored verbatim; control characters other than new lines are rejected. The body may be at most 20 KB.
    //
    //Future<DmcaNoticeAckEnvelope> submitCounterNotice(String id, SubmitCounterNoticeRequest submitCounterNoticeRequest) async
    test('test submitCounterNotice', () async {
      // TODO
    });

    // Submit a copyright (DMCA) notice
    //
    // Public. Always answers 202 with a reference id, whether or not the reference matched a story (it never reveals which stories exist); unmatched notices are handled manually. Free text is stored verbatim; control characters other than new lines are rejected. Rate limited per client address (5 per rolling 24 hours, 429 rate_limited with Retry-After). The body may be at most 20 KB (413 payload_too_large). The claimant gets a plain-text acknowledgement email.
    //
    //Future<DmcaNoticeAckEnvelope> submitDmcaNotice(SubmitDmcaNoticeRequest submitDmcaNoticeRequest) async
    test('test submitDmcaNotice', () async {
      // TODO
    });

  });
}
