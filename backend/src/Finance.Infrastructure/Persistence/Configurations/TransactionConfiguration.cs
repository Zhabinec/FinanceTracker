using Finance.Domain.Categories;
using Finance.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using FinanceTransaction = Finance.Domain.Transactions.Transaction;

namespace Finance.Infrastructure.Persistence.Configurations;

public sealed class TransactionConfiguration : IEntityTypeConfiguration<FinanceTransaction>
{
    public void Configure(EntityTypeBuilder<FinanceTransaction> builder)
    {
        builder.ToTable("transactions");

        builder.HasKey(transaction => transaction.Id);
        builder.Property(transaction => transaction.Version).IsConcurrencyToken();

        builder.Property(transaction => transaction.Type)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(transaction => transaction.Amount)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(transaction => transaction.Description)
            .HasMaxLength(500);

        builder.Property(transaction => transaction.OccurredAtUtc)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(transaction => transaction.CreatedAtUtc)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(transaction => transaction.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Category>()
            .WithMany()
            .HasForeignKey(transaction => new
            {
                transaction.CategoryId,
                transaction.UserId,
                transaction.Type
            })
            .HasPrincipalKey(category => new
            {
                category.Id,
                category.UserId,
                category.Type
            })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(transaction => new
        {
            transaction.UserId,
            transaction.OccurredAtUtc
        });

        builder.ToTable(table => table.HasCheckConstraint(
            "ck_transactions_amount_positive",
            "\"Amount\" > 0"));
    }
}
