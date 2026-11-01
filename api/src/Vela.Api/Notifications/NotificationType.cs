namespace Vela.Api.Notifications;

public enum NotificationType { Reply, Follower, NewStory }

public static class NotificationTypes
{
    public const string Reply = nameof(NotificationType.Reply);
    public const string Follower = nameof(NotificationType.Follower);
    public const string NewStory = nameof(NotificationType.NewStory);

    /// <summary>The contract's lowercase name for a stored type.</summary>
    public static string ToContract(string stored) => stored switch
    {
        Follower => "follow",
        NewStory => "new_story",
        _ => "reply",
    };
}
