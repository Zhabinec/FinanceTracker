using Finance.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Finance.Infrastructure.Persistence.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");

        builder.HasKey(user => user.Id);

        builder.Property(user => user.Email)
            .HasMaxLength(320)
            .IsRequired(false);

        builder.Property(user => user.NormalizedEmail)
            .HasMaxLength(320)
            .IsRequired(false);
        builder.ToTable(table => table.HasCheckConstraint("ck_user_email_pair",
            "(\"Email\" IS NULL AND \"NormalizedEmail\" IS NULL) OR (\"Email\" IS NOT NULL AND \"NormalizedEmail\" IS NOT NULL)"));

        builder.HasIndex(user => user.NormalizedEmail)
            .IsUnique();

        builder.Property(user => user.PasswordHash)
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(user => user.RegisteredAtUtc)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(user => user.BaseCurrency)
            .HasColumnType("character(3)")
            .IsRequired();
    }
}
