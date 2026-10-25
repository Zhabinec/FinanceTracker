using Finance.Domain.Accounts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Finance.Infrastructure.Persistence.Configurations;

public sealed class AccountTransferConfiguration : IEntityTypeConfiguration<AccountTransfer>
{
    public void Configure(EntityTypeBuilder<AccountTransfer> builder)
    {
        builder.ToTable("account_transfers", table => {
            table.HasCheckConstraint("ck_account_transfers_amount_positive", "\"Amount\" > 0");
            table.HasCheckConstraint("ck_account_transfers_different_accounts", "\"SourceAccountId\" <> \"TargetAccountId\"");
        });
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Version).IsConcurrencyToken();
        builder.Property(t => t.Amount).HasPrecision(18, 2);
        builder.Property(t => t.Currency).HasMaxLength(3).IsRequired();
        builder.Property(t => t.Description).HasMaxLength(500);
        builder.HasIndex(t => new { t.UserId, t.RequestId }).IsUnique();
        builder.HasIndex(t => new { t.UserId, t.CreatedAtUtc });
        builder.HasOne<Account>().WithMany().HasForeignKey(t => new { t.SourceAccountId, t.UserId })
            .HasPrincipalKey(a => new { a.Id, a.UserId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Account>().WithMany().HasForeignKey(t => new { t.TargetAccountId, t.UserId })
            .HasPrincipalKey(a => new { a.Id, a.UserId }).OnDelete(DeleteBehavior.Restrict);
    }
}
