using Microsoft.EntityFrameworkCore;
using Vela.Api.Data.Entities;

namespace Vela.Api.Data;

public class VelaDbContext(DbContextOptions<VelaDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Identity> Identities => Set<Identity>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<EmailCode> EmailCodes => Set<EmailCode>();
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
        b.Entity<EmailCode>(e => e.HasIndex(x => new { x.Email, x.CreatedAt }));
    }

    private static void ConfigureProfiles(ModelBuilder b) =>
        b.Entity<Profile>(e =>
        {
            e.HasKey(x => x.UserId);
            e.HasOne<User>().WithOne().HasForeignKey<Profile>(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.Handle).IsUnique();
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
            e.HasIndex(x => new { x.Status, x.PublishedAt, x.Id });
        });
        b.Entity<Chapter>(e =>
        {
            e.HasKey(x => new { x.WorkId, x.Index });
            e.HasOne<Work>().WithMany().HasForeignKey(x => x.WorkId).OnDelete(DeleteBehavior.Cascade);
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
        });
        b.Entity<Inquiry>(e =>
        {
            e.HasOne<User>().WithMany().HasForeignKey(x => x.SenderId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.ArtistId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.ArtistId);
        });
        b.Entity<Report>(e =>
        {
            e.HasOne<User>().WithMany().HasForeignKey(x => x.ReporterId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.Status, x.CreatedAt });
            e.HasIndex(x => new { x.ReporterId, x.CreatedAt });
        });
        b.Entity<ExportJob>(e =>
        {
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.UserId, x.RequestedAt });
        });
    }

    private static Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<T, string> LowerEnum<T>()
        where T : struct, Enum =>
        new(v => v.ToString().ToLowerInvariant(), s => Enum.Parse<T>(s, true));
}
