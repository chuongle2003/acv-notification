using Xunit;

namespace TaskTracker.Domain.Tests;

public class TaskRowTests
{
    [Fact]
    public void TaskRow_Init_ShouldSetProperties()
    {
        var row = new TaskRow
        {
            SourceFileId = "file1",
            LogicalRowKey = "key1",
            SheetName = "TUAN 33",
            SourceRowNumber = 5
        };

        Assert.Equal("file1", row.SourceFileId);
        Assert.Equal("key1", row.LogicalRowKey);
        Assert.Equal("TUAN 33", row.SheetName);
        Assert.Equal(5, row.SourceRowNumber);
        Assert.False(row.IsCompleted);
        Assert.Equal(TaskStatus.Unknown, row.CurrentStatus);
    }
}
