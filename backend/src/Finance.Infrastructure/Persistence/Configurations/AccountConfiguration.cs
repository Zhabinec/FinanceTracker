using Finance.Domain.Accounts;
using Finance.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Finance.Infrastructure.Persistence.Configurations;

public sealed class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> builder)
    {
        builder.ToTable("accounts");
        builder.HasKey(a => a.Id);
        builder.HasAlternateKey(a => new { a.Id, a.UserId });
        builder.Property(a => a.Name).HasMaxLength(100).IsRequired();
        builder.Property(a => a.NormalizedName).HasMaxLength(100).IsRequired();
        builder.Property(a => a.Currency).HasMaxLength(3).IsRequired();
        builder.Property(a => a.OpeningBalance).HasPrecision(18, 2);
        builder.Property(a => a.Version).IsConcurrencyToken();
        builder.HasIndex(a => new { a.UserId, a.NormalizedName }).IsUnique();
        builder.HasOne<User>().WithMany().HasForeignKey(a => a.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}
