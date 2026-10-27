using Finance.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Finance.Infrastructure.Persistence.Configurations;
public sealed class GoogleAccountLinkConfiguration : IEntityTypeConfiguration<GoogleAccountLink>
{
    public void Configure(EntityTypeBuilder<GoogleAccountLink> builder)
    {
        builder.ToTable("google_account_links");
        builder.HasKey(x => x.UserId);
        builder.Property(x => x.Subject).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.Subject).IsUnique();
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}
