using TaskTracker.Domain;
using TaskTracker.Presentation;
using Xunit;
using TaskStatus = TaskTracker.Domain.TaskStatus;

namespace TaskTracker.Windows.Tests;

public class TaskListPresentationTests
{
    [Fact]
    public void DefaultSelections_UseConcreteAllValues()
    {
        var filters = new TaskFilterState();

        Assert.Equal(string.Empty, filters.SelectedSheet);
        Assert.Equal(string.Empty, filters.SelectedHandler);
        Assert.Equal(TaskStatus.Unknown, filters.SelectedStatus);
        Assert.False(filters.HasActiveFilters);
    }

    [Fact]
    public void FilterOptions_ExposeTheirLabelsToAccessibility()
    {
        Assert.Equal("Tất cả", new TextFilterOption("Tất cả", "").ToString());
        Assert.Equal(
            "Quá hạn",
            new StatusFilterOption("Quá hạn", TaskStatus.Overdue).ToString());
    }

    [Theory]
    [InlineData(TaskStatus.Overdue, "Quá hạn")]
    [InlineData(TaskStatus.DueToday, "Đến hạn hôm nay")]
    [InlineData(TaskStatus.DueSoon, "Sắp đến hạn")]
    [InlineData(TaskStatus.NeedsReview, "Cần rà soát")]
    [InlineData(TaskStatus.Normal, "Bình thường")]
    [InlineData(TaskStatus.Completed, "Đã hoàn thành")]
    [InlineData(TaskStatus.Unknown, "Chưa xác định")]
    public void StatusLabel_IsAlwaysVietnamese(TaskStatus status, string expected)
    {
        Assert.Equal(expected, TaskStatusDisplay.GetLabel(status));
    }

    [Fact]
    public void Clear_RemovesEveryTaskFilter()
    {
        var filters = new TaskFilterState
        {
            SearchText = "công văn",
            ShowUnreadOnly = true,
            SelectedSheet = "TUAN 33",
            SelectedHandler = "Nguyễn Văn A",
            SelectedStatus = TaskStatus.Overdue
        };

        Assert.True(filters.HasActiveFilters);

        filters.Clear();

        Assert.Equal(string.Empty, filters.SearchText);
        Assert.False(filters.ShowUnreadOnly);
        Assert.Equal(string.Empty, filters.SelectedSheet);
        Assert.Equal(string.Empty, filters.SelectedHandler);
        Assert.Equal(TaskStatus.Unknown, filters.SelectedStatus);
        Assert.False(filters.HasActiveFilters);
    }

    [Theory]
    [InlineData(ResolutionSource.Parser, "Tự nhận diện")]
    [InlineData(ResolutionSource.KeepExcelDate, "Giữ ngày Excel")]
    [InlineData(ResolutionSource.UseSwappedDate, "Đảo ngày/tháng")]
    [InlineData(ResolutionSource.ManualDate, "Nhập thủ công")]
    [InlineData(ResolutionSource.UnresolvedByUser, "Chưa xác định")]
    public void ResolutionSourceLabel_ExplainsHowTheDateWasChosen(
        ResolutionSource source,
        string expected)
    {
        Assert.Equal(expected, ResolutionSourceDisplay.GetLabel(source));
    }
}
