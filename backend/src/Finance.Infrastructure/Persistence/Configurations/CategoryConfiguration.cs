using Finance.Domain.Categories;
using Finance.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Finance.Infrastructure.Persistence.Configurations;

public sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.ToTable("categories");

        builder.HasKey(category => category.Id);

        builder.HasAlternateKey(category => new
        {
            category.Id,
            category.UserId,
            category.Type
        });

        builder.Property(category => category.Name)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(category => category.Type)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(category => category.IsActive)
            .IsRequired();

        builder.HasIndex(category => new
            {
                category.UserId,
                category.Type,
                category.Name
            })
            .IsUnique();

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(category => category.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
