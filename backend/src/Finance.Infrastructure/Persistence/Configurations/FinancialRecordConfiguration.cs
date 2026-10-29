using Finance.Domain.Ledger;
using Finance.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Finance.Infrastructure.Persistence.Configurations;

public sealed class FinancialRecordConfiguration : IEntityTypeConfiguration<FinancialRecord>
{
    public void Configure(EntityTypeBuilder<FinancialRecord> b)
    {
        b.ToTable("financial_records", t => {
            t.HasCheckConstraint("ck_financial_record_kind", "\"Kind\" BETWEEN 1 AND 8");
            t.HasCheckConstraint("ck_financial_record_envelope", "octet_length(\"Nonce\") = 12 AND octet_length(\"Ciphertext\") BETWEEN 17 AND 16384");
        });
        b.HasKey(x => new { x.UserId, x.Kind, x.Id });
        b.Property(x => x.Version).IsConcurrencyToken();
        b.Property(x => x.Nonce).IsRequired();
        b.Property(x => x.Ciphertext).IsRequired();
        b.HasIndex(x => new { x.UserId, x.Kind, x.OccurredOn, x.CreatedAtUtc, x.Id });
        b.HasIndex(x => new { x.UserId, x.Kind, x.CategoryId, x.OccurredOn });
        b.HasIndex(x => new { x.UserId, x.Kind, x.AccountId, x.OccurredOn });
        b.HasOne<FinancialWorkspace>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
public sealed class FinancialWorkspaceConfiguration : IEntityTypeConfiguration<FinancialWorkspace>
{
    public void Configure(EntityTypeBuilder<FinancialWorkspace> b)
    {
        b.ToTable("financial_workspaces");
        b.HasKey(x => x.UserId);
        b.Property(x => x.Revision).IsConcurrencyToken();
        b.Property(x => x.BaseCurrency).HasMaxLength(3).IsRequired();
        b.HasOne<User>().WithOne().HasForeignKey<FinancialWorkspace>(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
