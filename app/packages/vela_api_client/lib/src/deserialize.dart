import 'package:vela_api_client/src/model/add_reply_request.dart';
import 'package:vela_api_client/src/model/artist_summary.dart';
import 'package:vela_api_client/src/model/chapter.dart';
import 'package:vela_api_client/src/model/complete_onboarding_request.dart';
import 'package:vela_api_client/src/model/create_draft_request.dart';
import 'package:vela_api_client/src/model/create_draft_result.dart';
import 'package:vela_api_client/src/model/create_draft_result_envelope.dart';
import 'package:vela_api_client/src/model/dmca_notice_ack.dart';
import 'package:vela_api_client/src/model/dmca_notice_ack_envelope.dart';
import 'package:vela_api_client/src/model/email_request_request.dart';
import 'package:vela_api_client/src/model/email_verify_request.dart';
import 'package:vela_api_client/src/model/envelope.dart';
import 'package:vela_api_client/src/model/error_body.dart';
import 'package:vela_api_client/src/model/export_job.dart';
import 'package:vela_api_client/src/model/export_job_envelope.dart';
import 'package:vela_api_client/src/model/inquiry.dart';
import 'package:vela_api_client/src/model/inquiry_envelope.dart';
import 'package:vela_api_client/src/model/logout_request.dart';
import 'package:vela_api_client/src/model/mark_read_request.dart';
import 'package:vela_api_client/src/model/me.dart';
import 'package:vela_api_client/src/model/me_envelope.dart';
import 'package:vela_api_client/src/model/me_tier.dart';
import 'package:vela_api_client/src/model/notification.dart';
import 'package:vela_api_client/src/model/notification_list_envelope.dart';
import 'package:vela_api_client/src/model/page.dart';
import 'package:vela_api_client/src/model/profile.dart';
import 'package:vela_api_client/src/model/profile_envelope.dart';
import 'package:vela_api_client/src/model/profile_list_envelope.dart';
import 'package:vela_api_client/src/model/refresh_request.dart';
import 'package:vela_api_client/src/model/reply.dart';
import 'package:vela_api_client/src/model/reply_envelope.dart';
import 'package:vela_api_client/src/model/reply_list_envelope.dart';
import 'package:vela_api_client/src/model/report_request.dart';
import 'package:vela_api_client/src/model/search_results.dart';
import 'package:vela_api_client/src/model/search_results_list_envelope.dart';
import 'package:vela_api_client/src/model/send_inquiry_request.dart';
import 'package:vela_api_client/src/model/service_item.dart';
import 'package:vela_api_client/src/model/show_item.dart';
import 'package:vela_api_client/src/model/sso_sign_in_request.dart';
import 'package:vela_api_client/src/model/submit_dmca_notice_request.dart';
import 'package:vela_api_client/src/model/token_pair.dart';
import 'package:vela_api_client/src/model/token_pair_envelope.dart';
import 'package:vela_api_client/src/model/unread_count.dart';
import 'package:vela_api_client/src/model/unread_count_envelope.dart';
import 'package:vela_api_client/src/model/update_profile_request.dart';
import 'package:vela_api_client/src/model/update_work_request.dart';
import 'package:vela_api_client/src/model/upload_ticket.dart';
import 'package:vela_api_client/src/model/upload_ticket_envelope.dart';
import 'package:vela_api_client/src/model/work.dart';
import 'package:vela_api_client/src/model/work_envelope.dart';
import 'package:vela_api_client/src/model/work_list_envelope.dart';

final _regList = RegExp(r'^List<(.*)>$');
final _regSet = RegExp(r'^Set<(.*)>$');
final _regMap = RegExp(r'^Map<String,(.*)>$');

  ReturnType deserialize<ReturnType, BaseType>(dynamic value, String targetType, {bool growable= true}) {
      switch (targetType) {
        case 'String':
          return '$value' as ReturnType;
        case 'int':
          return (value is int ? value : int.parse('$value')) as ReturnType;
        case 'bool':
          if (value is bool) {
            return value as ReturnType;
          }
          final valueString = '$value'.toLowerCase();
          return (valueString == 'true' || valueString == '1') as ReturnType;
        case 'double':
          return (value is double ? value : double.parse('$value')) as ReturnType;
        case 'AddReplyRequest':
          return AddReplyRequest.fromJson(value as Map<String, dynamic>) as ReturnType;
        case 'ArtistSummary':
          return ArtistSummary.fromJson(value as Map<String, dynamic>) as ReturnType;
        case 'Chapter':
          return Chapter.fromJson(value as Map<String, dynamic>) as ReturnType;
        case 'CompleteOnboardingRequest':
          return CompleteOnboardingRequest.fromJson(value as Map<String, dynamic>) as ReturnType;
        case 'CreateDraftRequest':
          return CreateDraftRequest.fromJson(value as Map<String, dynamic>) as ReturnType;
        case 'CreateDraftResult':
          return CreateDraftResult.fromJson(value as Map<String, dynamic>) as ReturnType;
        case 'CreateDraftResultEnvelope':
          return CreateDraftResultEnvelope.fromJson(value as Map<String, dynamic>) as ReturnType;
        case 'DmcaNoticeAck':
          return DmcaNoticeAck.fromJson(value as Map<String, dynamic>) as ReturnType;
        case 'DmcaNoticeAckEnvelope':
          return DmcaNoticeAckEnvelope.fromJson(value as Map<String, dynamic>) as ReturnType;
        case 'EmailRequestRequest':
          return EmailRequestRequest.fromJson(value as Map<String, dynamic>) as ReturnType;
        case 'EmailVerifyRequest':
          return EmailVerifyRequest.fromJson(value as Map<String, dynamic>) as ReturnType;
        case 'Envelope':
          return Envelope.fromJson(value as Map<String, dynamic>) as ReturnType;
        case 'ErrorBody':
          return ErrorBody.fromJson(value as Map<String, dynamic>) as ReturnType;
        case 'ExportJob':
          return ExportJob.fromJson(value as Map<String, dynamic>) as ReturnType;
        case 'ExportJobEnvelope':
          return ExportJobEnvelope.fromJson(value as Map<String, dynamic>) as ReturnType;
        case 'Inquiry':
          return Inquiry.fromJson(value as Map<String, dynamic>) as ReturnType;
        case 'InquiryEnvelope':
          return InquiryEnvelope.fromJson(value as Map<String, dynamic>) as ReturnType;
        case 'LogoutRequest':
          return LogoutRequest.fromJson(value as Map<String, dynamic>) as ReturnType;
        case 'MarkReadRequest':
          return MarkReadRequest.fromJson(value as Map<String, dynamic>) as ReturnType;
        case 'Me':
          return Me.fromJson(value as Map<String, dynamic>) as ReturnType;
        case 'MeEnvelope':
          return MeEnvelope.fromJson(value as Map<String, dynamic>) as ReturnType;
        case 'MeTier':
          return MeTier.fromJson(value as Map<String, dynamic>) as ReturnType;
        case 'Notification':
          return Notification.fromJson(value as Map<String, dynamic>) as ReturnType;
        case 'NotificationListEnvelope':
          return NotificationListEnvelope.fromJson(value as Map<String, dynamic>) as ReturnType;
        case 'Page':
          return Page.fromJson(value as Map<String, dynamic>) as ReturnType;
        case 'Profile':
          return Profile.fromJson(value as Map<String, dynamic>) as ReturnType;
        case 'ProfileEnvelope':
          return ProfileEnvelope.fromJson(value as Map<String, dynamic>) as ReturnType;
        case 'ProfileListEnvelope':
          return ProfileListEnvelope.fromJson(value as Map<String, dynamic>) as ReturnType;
        case 'RefreshRequest':
          return RefreshRequest.fromJson(value as Map<String, dynamic>) as ReturnType;
        case 'Reply':
          return Reply.fromJson(value as Map<String, dynamic>) as ReturnType;
        case 'ReplyEnvelope':
          return ReplyEnvelope.fromJson(value as Map<String, dynamic>) as ReturnType;
        case 'ReplyListEnvelope':
          return ReplyListEnvelope.fromJson(value as Map<String, dynamic>) as ReturnType;
        case 'ReportRequest':
          return ReportRequest.fromJson(value as Map<String, dynamic>) as ReturnType;
        case 'SearchResults':
          return SearchResults.fromJson(value as Map<String, dynamic>) as ReturnType;
        case 'SearchResultsListEnvelope':
          return SearchResultsListEnvelope.fromJson(value as Map<String, dynamic>) as ReturnType;
        case 'SendInquiryRequest':
          return SendInquiryRequest.fromJson(value as Map<String, dynamic>) as ReturnType;
        case 'ServiceItem':
          return ServiceItem.fromJson(value as Map<String, dynamic>) as ReturnType;
        case 'ShowItem':
          return ShowItem.fromJson(value as Map<String, dynamic>) as ReturnType;
        case 'SsoSignInRequest':
          return SsoSignInRequest.fromJson(value as Map<String, dynamic>) as ReturnType;
        case 'SubmitDmcaNoticeRequest':
          return SubmitDmcaNoticeRequest.fromJson(value as Map<String, dynamic>) as ReturnType;
        case 'TokenPair':
          return TokenPair.fromJson(value as Map<String, dynamic>) as ReturnType;
        case 'TokenPairEnvelope':
          return TokenPairEnvelope.fromJson(value as Map<String, dynamic>) as ReturnType;
        case 'UnreadCount':
          return UnreadCount.fromJson(value as Map<String, dynamic>) as ReturnType;
        case 'UnreadCountEnvelope':
          return UnreadCountEnvelope.fromJson(value as Map<String, dynamic>) as ReturnType;
        case 'UpdateProfileRequest':
          return UpdateProfileRequest.fromJson(value as Map<String, dynamic>) as ReturnType;
        case 'UpdateWorkRequest':
          return UpdateWorkRequest.fromJson(value as Map<String, dynamic>) as ReturnType;
        case 'UploadTicket':
          return UploadTicket.fromJson(value as Map<String, dynamic>) as ReturnType;
        case 'UploadTicketEnvelope':
          return UploadTicketEnvelope.fromJson(value as Map<String, dynamic>) as ReturnType;
        case 'Work':
          return Work.fromJson(value as Map<String, dynamic>) as ReturnType;
        case 'WorkEnvelope':
          return WorkEnvelope.fromJson(value as Map<String, dynamic>) as ReturnType;
        case 'WorkListEnvelope':
          return WorkListEnvelope.fromJson(value as Map<String, dynamic>) as ReturnType;
        default:
          RegExpMatch? match;

          if (value is List && (match = _regList.firstMatch(targetType)) != null) {
            targetType = match![1]!; // ignore: parameter_assignments
            return value
              .map<BaseType>((dynamic v) => deserialize<BaseType, BaseType>(v, targetType, growable: growable))
              .toList(growable: growable) as ReturnType;
          }
          if (value is Set && (match = _regSet.firstMatch(targetType)) != null) {
            targetType = match![1]!; // ignore: parameter_assignments
            return value
              .map<BaseType>((dynamic v) => deserialize<BaseType, BaseType>(v, targetType, growable: growable))
              .toSet() as ReturnType;
          }
          if (value is Map && (match = _regMap.firstMatch(targetType)) != null) {
            targetType = match![1]!.trim(); // ignore: parameter_assignments
            return Map<String, BaseType>.fromIterables(
              value.keys as Iterable<String>,
              value.values.map((dynamic v) => deserialize<BaseType, BaseType>(v, targetType, growable: growable)),
            ) as ReturnType;
          }
          break;
    }
    throw Exception('Cannot deserialize');
  }