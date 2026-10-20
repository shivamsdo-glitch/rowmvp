using Aurigo.RowManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aurigo.RowManagement.Infrastructure.Persistence.Configurations;

public class ParcelQuestionnaireConfiguration : IEntityTypeConfiguration<ParcelQuestionnaire>
{
    public void Configure(EntityTypeBuilder<ParcelQuestionnaire> b)
    {
        b.ToTable("parcel_questionnaires");
        b.HasKey(x => x.Id);
        b.Property(x => x.Number).IsRequired().HasMaxLength(80);
        b.Property(x => x.RelocationType).IsRequired().HasMaxLength(100);
        b.HasOne<Parcel>().WithMany().HasForeignKey(x => x.ParcelId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Interests).WithOne().HasForeignKey(c => c.QuestionnaireId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Questions).WithOne().HasForeignKey(c => c.QuestionnaireId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.ParcelId, x.Sequence }).IsUnique();
    }
}

public class QuestionnaireInterestConfiguration : IEntityTypeConfiguration<QuestionnaireInterest>
{
    public void Configure(EntityTypeBuilder<QuestionnaireInterest> b)
    {
        b.ToTable("questionnaire_interests");
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).IsRequired().HasMaxLength(200);
        b.Property(x => x.Phone).HasMaxLength(100);
        b.Property(x => x.Contact).HasMaxLength(200);
        b.Property(x => x.Email).HasMaxLength(200);
        b.Property(x => x.Address).HasMaxLength(500);
    }
}

public class QuestionnaireQuestionConfiguration : IEntityTypeConfiguration<QuestionnaireQuestion>
{
    public void Configure(EntityTypeBuilder<QuestionnaireQuestion> b)
    {
        b.ToTable("questionnaire_questions");
        b.HasKey(x => x.Id);
        b.Property(x => x.Question).IsRequired().HasMaxLength(1000);
        b.Property(x => x.Answer).HasMaxLength(4000);
        b.Property(x => x.Notes).HasMaxLength(4000);
    }
}
