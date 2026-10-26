using Finance.Domain.Budgets;
using Finance.Domain.Categories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Finance.Infrastructure.Persistence.Configurations;

public sealed class MonthlyBudgetConfiguration : IEntityTypeConfiguration<MonthlyBudget>
{
    public void Configure(EntityTypeBuilder<MonthlyBudget> builder)
    {
        builder.ToTable("monthly_budgets", table =>
        {
            table.HasCheckConstraint("ck_budgets_positive", "\"Limit\" > 0");
            table.HasCheckConstraint("ck_budgets_expense", "\"Type\" = 2");
            table.HasCheckConstraint("ck_budgets_month", "EXTRACT(DAY FROM \"Month\") = 1 AND \"Month\" >= DATE '1900-01-01' AND \"Month\" < DATE '9999-01-01'");
        });
        builder.HasKey(b => b.Id);
        builder.Property(b => b.Limit).HasPrecision(18, 2);
        builder.Property(b => b.Type).HasConversion<int>();
        builder.Property(b => b.Version).IsConcurrencyToken();
        builder.HasIndex(b => new { b.UserId, b.Month, b.CategoryId }).IsUnique();
        builder.HasOne<Category>().WithMany().HasForeignKey(b => new { b.CategoryId, b.UserId, b.Type })
            .HasPrincipalKey(c => new { c.Id, c.UserId, c.Type }).OnDelete(DeleteBehavior.Restrict);
    }
}
