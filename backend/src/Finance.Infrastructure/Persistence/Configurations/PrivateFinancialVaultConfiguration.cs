using Finance.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Finance.Infrastructure.Persistence.Configurations;

public sealed class PrivateFinancialVaultConfiguration : IEntityTypeConfiguration<PrivateFinancialVault>
{
    public void Configure(EntityTypeBuilder<PrivateFinancialVault> builder)
    {
        builder.ToTable("private_financial_vaults");
        builder.HasKey(v => v.UserId);
        builder.HasIndex(v => v.Id).IsUnique();
        builder.Property(v => v.Revision).IsConcurrencyToken();
        builder.Property(v => v.Nonce).HasMaxLength(16).IsRequired();
        builder.Property(v => v.Ciphertext).HasMaxLength(2_666_688).IsRequired();
        builder.Property(v => v.LegacyFingerprint).HasMaxLength(64).IsRequired();
        builder.HasOne<User>().WithMany().HasForeignKey(v => v.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.ToTable(t => t.HasCheckConstraint("ck_private_vault_revision", "\"Revision\" > 0 AND \"Revision\" <= 9007199254740990"));
    }
}
