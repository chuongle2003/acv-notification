using TaskTracker.Domain;
using TaskTracker.Presentation;
using Xunit;

namespace TaskTracker.Windows.Tests;

public class DeadlineReviewPresentationTests
{
    [Fact]
    public void AvailableActions_FollowDeadlineCandidates()
    {
        Assert.False(DeadlineReviewActionAvailability.CanKeepExcelDate(null));
        Assert.False(DeadlineReviewActionAvailability.CanUseSwappedDate(null));

        Assert.True(DeadlineReviewActionAvailability.CanKeepExcelDate(new DateOnly(2026, 8, 4)));
        Assert.True(DeadlineReviewActionAvailability.CanUseSwappedDate(new DateOnly(2026, 4, 8)));
    }
}
