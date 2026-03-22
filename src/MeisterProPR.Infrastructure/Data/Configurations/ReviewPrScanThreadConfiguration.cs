using MeisterProPR.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MeisterProPR.Infrastructure.Data.Configurations;

internal sealed class ReviewPrScanThreadConfiguration : IEntityTypeConfiguration<ReviewPrScanThread>
{
    public void Configure(EntityTypeBuilder<ReviewPrScanThread> builder)
    {
        builder.ToTable("review_pr_scan_threads");

        builder.HasKey(t => new { t.ReviewPrScanId, t.ThreadId });

        builder.Property(t => t.ReviewPrScanId)
            .HasColumnName("review_pr_scan_id")
            .IsRequired();

        builder.Property(t => t.ThreadId)
            .HasColumnName("thread_id");

        builder.Property(t => t.LastSeenReplyCount)
            .HasColumnName("last_seen_reply_count")
            .HasDefaultValue(0);
    }
}
