using Finance.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Finance.Infrastructure.Persistence.Configurations;

public sealed class RefreshSessionConfiguration : IEntityTypeConfiguration<RefreshSession>
{
    public void Configure(EntityTypeBuilder<RefreshSession> builder)
    {
        builder.ToTable("refresh_sessions");
        builder.HasKey(session => session.Id);
        builder.Property(session => session.TokenHash).HasMaxLength(64).IsRequired();
        builder.HasIndex(session => session.TokenHash).IsUnique();
        builder.Property(session => session.CreatedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(session => session.ExpiresAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(session => session.RevokedAtUtc).HasColumnType("timestamp with time zone");
        builder.HasOne<User>().WithMany().HasForeignKey(session => session.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(session => new { session.UserId, session.ExpiresAtUtc });
    }
}
