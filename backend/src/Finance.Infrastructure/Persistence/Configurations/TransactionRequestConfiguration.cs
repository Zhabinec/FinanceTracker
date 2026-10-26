using Finance.Domain.Transactions;
using Finance.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Finance.Infrastructure.Persistence.Configurations;

public sealed class TransactionRequestConfiguration : IEntityTypeConfiguration<TransactionRequest>
{
    public void Configure(EntityTypeBuilder<TransactionRequest> builder)
    {
        builder.ToTable("transaction_requests");
        builder.HasKey(r => new { r.UserId, r.RequestId });
        builder.Property(r => r.Fingerprint).HasMaxLength(64).IsRequired();
        builder.HasOne<User>().WithMany().HasForeignKey(r => r.UserId).OnDelete(DeleteBehavior.Restrict);
        // TransactionId intentionally has no FK: the receipt outlives the financial record.
    }
}
