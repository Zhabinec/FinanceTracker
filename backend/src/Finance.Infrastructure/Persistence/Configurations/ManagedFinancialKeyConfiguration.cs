using Finance.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Finance.Infrastructure.Persistence.Configurations;
public sealed class ManagedFinancialKeyConfiguration : IEntityTypeConfiguration<ManagedFinancialKey>
{
    public void Configure(EntityTypeBuilder<ManagedFinancialKey> builder)
    {
        builder.ToTable("managed_financial_keys");
        builder.HasKey(x => x.UserId);
        builder.HasIndex(x => x.VaultId).IsUnique();
        builder.Property(x => x.KeyId).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Nonce).HasMaxLength(16).IsRequired();
        builder.Property(x => x.Ciphertext).HasMaxLength(256).IsRequired();
        builder.HasOne<User>().WithOne().HasForeignKey<ManagedFinancialKey>(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
