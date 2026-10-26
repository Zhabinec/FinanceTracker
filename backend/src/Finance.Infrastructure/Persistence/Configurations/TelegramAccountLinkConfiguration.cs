using Finance.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Finance.Infrastructure.Persistence.Configurations;

public sealed class TelegramAccountLinkConfiguration : IEntityTypeConfiguration<TelegramAccountLink>
{
    public void Configure(EntityTypeBuilder<TelegramAccountLink> builder)
    {
        builder.ToTable("telegram_account_links");
        builder.HasKey(link => link.UserId);
        builder.HasIndex(link => link.TelegramUserId).IsUnique();
        builder.Property(link => link.Version).IsConcurrencyToken();
        builder.HasOne<User>().WithMany().HasForeignKey(link => link.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.ToTable(table => table.HasCheckConstraint("ck_telegram_user_id_positive", "\"TelegramUserId\" > 0 AND \"TelegramUserId\" <= 4503599627370495"));
    }
}
