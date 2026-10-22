namespace Vela.Api.Admin;

public enum ModerationOutcome
{
    Ok, ReasonRequired, ReasonTooLong, NotFound, NotApplicable, Forbidden, CannotBanSelf, CannotBanAdmin,
    TargetStillBlocked, VariantsUnavailable, AlreadyResolved,
}
