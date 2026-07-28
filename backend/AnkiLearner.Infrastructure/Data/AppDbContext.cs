using AnkiLearner.Core.Entities;
using AnkiLearner.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace AnkiLearner.Infrastructure.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<UserSettings> UserSettings => Set<UserSettings>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<ApiToken> ApiTokens => Set<ApiToken>();
    public DbSet<Word> Words => Set<Word>();
    public DbSet<WordTranslation> WordTranslations => Set<WordTranslation>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<WordTag> WordTags => Set<WordTag>();
    public DbSet<SrsState> SrsStates => Set<SrsState>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<UserSettings>(e =>
        {
            e.HasKey(s => s.UserId);
            e.Property(s => s.LearningLanguage).HasMaxLength(20);
            // List<string> maps to a Postgres text[] column via Npgsql.
            e.HasOne<AppUser>().WithOne().HasForeignKey<UserSettings>(s => s.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<RefreshToken>(e =>
        {
            e.HasIndex(t => t.TokenHash).IsUnique();
            e.Property(t => t.TokenHash).HasMaxLength(88);
            e.HasOne<AppUser>().WithMany().HasForeignKey(t => t.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ApiToken>(e =>
        {
            e.HasIndex(t => t.TokenHash).IsUnique();
            e.Property(t => t.TokenHash).HasMaxLength(88);
            e.Property(t => t.Name).HasMaxLength(100);
            e.HasOne<AppUser>().WithMany().HasForeignKey(t => t.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Word>(e =>
        {
            e.Property(w => w.LanguageCode).HasMaxLength(20);
            e.Property(w => w.Transcription).HasMaxLength(200);
            e.Property(w => w.PartOfSpeech).HasMaxLength(50);
            e.Property(w => w.Gender).HasMaxLength(30);
            // Dictionary and study views always filter by user + current learning language.
            e.HasIndex(w => new { w.UserId, w.LanguageCode });
            e.HasIndex(w => new { w.UserId, w.LanguageCode, w.TermNormalized });
            e.HasOne<AppUser>().WithMany().HasForeignKey(w => w.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<WordTranslation>(e =>
        {
            e.Property(t => t.LanguageCode).HasMaxLength(20);
            e.HasIndex(t => new { t.WordId, t.LanguageCode }).IsUnique();
            e.HasOne(t => t.Word).WithMany(w => w.Translations).HasForeignKey(t => t.WordId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Tag>(e =>
        {
            e.Property(t => t.Name).HasMaxLength(100);
            e.HasIndex(t => new { t.UserId, t.Name }).IsUnique();
            e.HasOne<AppUser>().WithMany().HasForeignKey(t => t.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<WordTag>(e =>
        {
            e.HasKey(wt => new { wt.WordId, wt.TagId });
            e.HasOne(wt => wt.Word).WithMany(w => w.WordTags).HasForeignKey(wt => wt.WordId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(wt => wt.Tag).WithMany(t => t.WordTags).HasForeignKey(wt => wt.TagId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<SrsState>(e =>
        {
            // Stored as a string for database readability.
            e.Property(s => s.Exercise).HasConversion<string>().HasMaxLength(30);
            e.HasIndex(s => new { s.WordId, s.Exercise }).IsUnique();
            // Study-next scans by exercise + due date.
            e.HasIndex(s => new { s.Exercise, s.Due });
            e.HasOne(s => s.Word).WithMany(w => w.SrsStates).HasForeignKey(s => s.WordId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
