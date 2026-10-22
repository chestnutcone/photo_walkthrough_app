using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Vela.Api.Data.Entities;

namespace Vela.Api.Data;

public class VelaDbContext(DbContextOptions<VelaDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Identity> Identities => Set<Identity>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<EmailCode> EmailCodes => Set<EmailCode>();
    public DbSet<VerifyAttempt> VerifyAttempts => Set<VerifyAttempt>();
    public DbSet<Tier> Tiers => Set<Tier>();
    public DbSet<Profile> Profiles => Set<Profile>();
    public DbSet<Follow> Follows => Set<Follow>();
    public DbSet<Work> Works => Set<Work>();
    public DbSet<Chapter> Chapters => Set<Chapter>();
    public DbSet<Reply> Replies => Set<Reply>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<Inquiry> Inquiries => Set<Inquiry>();
    public DbSet<Report> Reports => Set<Report>();
    public DbSet<Block> Blocks => Set<Block>();
    public DbSet<ExportJob> ExportJobs => Set<ExportJob>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasPostgresExtension("pg_trgm");
        ConfigureAccounts(b);
        ConfigureProfiles(b);
        ConfigureWorks(b);
        ConfigureSocial(b);
    }

    private static void ConfigureAccounts(ModelBuilder b)
    {
        b.Entity<Tier>(e =>
        {
            e.HasIndex(x => x.Name).IsUnique();
            e.HasData(new Tier { Id = Tier.FreemiumId, Name = "Freemium", MaxWorks = 20 });
        });
        b.Entity<User>(e =>
        {
            e.HasOne<Tier>().WithMany().HasForeignKey(x => x.TierId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.DeletedAt).HasFilter("deleted_at IS NOT NULL").HasDatabaseName("ix_users_deleted_at_pending");
        });
        b.Entity<Identity>(e =>
        {
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.Provider, x.Subject }).IsUnique();
            e.HasIndex(x => x.UserId);
        });
        b.Entity<RefreshToken>(e =>
        {
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasIndex(x => x.FamilyId);
            e.HasIndex(x => x.UserId);
        });
        b.Entity<EmailCode>(e =>
        {
            e.HasIndex(x => new { x.Email, x.CreatedAt });
            e.HasIndex(x => new { x.RequestIp, x.CreatedAt });
        });
        b.Entity<VerifyAttempt>(e =>
        {
            e.HasIndex(x => new { x.Ip, x.Kind, x.CreatedAt });
            e.HasIndex(x => new { x.Email, x.Kind, x.CreatedAt });
        });
    }

    private static void ConfigureProfiles(ModelBuilder b) =>
        b.Entity<Profile>(e =>
        {
            e.HasKey(x => x.UserId);
            e.HasOne<User>().WithOne().HasForeignKey<Profile>(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.Handle).IsUnique();
            e.Property(x => x.SearchText).HasComputedColumnSql(
                "lower(handle || ' ' || display_name || ' ' || city || ' ' || discipline)", stored: true);
            e.HasIndex(x => x.SearchText).HasMethod("gin").HasOperators("gin_trgm_ops").HasDatabaseName("ix_profiles_search_text_trgm");
            e.ToTable(t => t.HasCheckConstraint("ck_profiles_handle_lowercase", "handle = lower(handle)"));
            e.ComplexCollection(x => x.Services, c => c.ToJson("services"));
            e.ComplexCollection(x => x.Shows, c => c.ToJson("shows"));
        });

    private static void ConfigureWorks(ModelBuilder b)
    {
        b.Entity<Work>(e =>
        {
            e.HasOne<User>().WithMany().HasForeignKey(x => x.ArtistId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Status).HasConversion(LowerEnum<WorkStatus>()).HasMaxLength(16);
            e.HasIndex(x => new { x.ArtistId, x.Status });
            // Filled by triggers (see AddSearchIndexes), so EF must neither insert nor update it.
            e.Property(x => x.SearchText).HasDefaultValue("");
            e.Property(x => x.SearchText).Metadata.SetBeforeSaveBehavior(PropertySaveBehavior.Ignore);
            e.Property(x => x.SearchText).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
            e.HasIndex(x => x.SearchText).HasMethod("gin").HasOperators("gin_trgm_ops").HasDatabaseName("ix_works_search_text_trgm");
            e.HasIndex(x => new { x.Status, x.PublishedAt, x.Id });
            e.HasIndex(x => new { x.ArtistId, x.PublishedAt, x.Id }).HasFilter("status = 'published'");
        });
        b.Entity<Chapter>(e =>
        {
            e.HasKey(x => new { x.WorkId, x.Index });
            e.HasOne<Work>().WithMany().HasForeignKey(x => x.WorkId).OnDelete(DeleteBehavior.Cascade);
            e.ToTable(t =>
            {
                t.HasCheckConstraint("ck_chapters_x_range", "x BETWEEN 0 AND 1");
                t.HasCheckConstraint("ck_chapters_y_range", "y BETWEEN 0 AND 1");
                t.HasCheckConstraint("ck_chapters_zoom_positive", "zoom IS NULL OR zoom > 0");
                t.HasCheckConstraint("ck_chapters_index_range", "\"index\" BETWEEN 0 AND 39");
            });
        });
        b.Entity<Reply>(e =>
        {
            e.HasOne<Work>().WithMany().HasForeignKey(x => x.WorkId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.AuthorId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.HiddenBy).HasConversion(LowerEnum<HiddenBy>()).HasMaxLength(16);
            e.HasIndex(x => new { x.WorkId, x.ChapterIndex, x.CreatedAt });
            e.HasIndex(x => x.AuthorId);
        });
    }

    private static void ConfigureSocial(ModelBuilder b)
    {
        b.Entity<Follow>(e =>
        {
            e.HasKey(x => new { x.FollowerId, x.FolloweeId });
            e.HasOne<User>().WithMany().HasForeignKey(x => x.FollowerId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.FolloweeId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.FolloweeId);
        });
        b.Entity<Block>(e =>
        {
            e.HasKey(x => new { x.BlockerId, x.BlockedId });
            e.HasOne<User>().WithMany().HasForeignKey(x => x.BlockerId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.BlockedId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.BlockedId);
        });
        b.Entity<Notification>(e =>
        {
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.UserId, x.CreatedAt, x.Id });
            // One new-story notification per recipient and work, so a repeated WorkPublished (admin restore) adds nothing.
            e.HasIndex(x => new { x.UserId, x.Type, x.WorkId }).IsUnique()
                .HasFilter("type = 'NewStory'").HasDatabaseName("ux_notifications_new_story");
        });
        b.Entity<Inquiry>(e =>
        {
            e.HasOne<User>().WithMany().HasForeignKey(x => x.SenderId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.ArtistId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.ArtistId);
            e.HasIndex(x => new { x.SenderId, x.CreatedAt });
            e.HasIndex(x => new { x.ClientIp, x.CreatedAt });
        });
        b.Entity<Report>(e =>
        {
            e.HasOne<User>().WithMany().HasForeignKey(x => x.ReporterId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(x => new { x.Status, x.CreatedAt });
            e.HasIndex(x => new { x.ReporterId, x.CreatedAt });
            e.Property(x => x.Note).HasMaxLength(500);
            // One report per reporter per target; one pending auto-flag per target (retries cannot duplicate queue items).
            e.HasIndex(x => new { x.ReporterId, x.TargetType, x.TargetId }).IsUnique()
                .HasFilter("reporter_id IS NOT NULL").HasDatabaseName("ux_reports_reporter_target");
            e.HasIndex(x => new { x.TargetType, x.TargetId }).IsUnique()
                .HasFilter("source = 'auto_flag' AND status = 'pending'").HasDatabaseName("ux_reports_pending_auto_flag");
        });
        b.Entity<ExportJob>(e =>
        {
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.UserId, x.RequestedAt });
            e.HasIndex(x => new { x.Status, x.RequestedAt });
        });
    }

    private static Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<T, string> LowerEnum<T>()
        where T : struct, Enum =>
        new(v => v.ToString().ToLowerInvariant(), s => Enum.Parse<T>(s, true));
}
