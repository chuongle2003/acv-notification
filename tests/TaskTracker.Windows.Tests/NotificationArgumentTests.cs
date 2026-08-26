using TaskTracker.Domain;
using TaskTracker.Windows.Notifications;
using Xunit;

namespace TaskTracker.Windows.Tests;

public class NotificationArgumentTests
{
    [Theory]
    [InlineData("action=ack&sourceFileId=f1&logicalRowKey=k1&deadlineVersion=v1&alertGroup=Overdue")]
    [InlineData("action=ack;sourceFileId=f1;logicalRowKey=k1;deadlineVersion=v1;alertGroup=Overdue")]
    public void ParseActivation_HandlesSupportedSeparators(string arguments)
    {
        var activation = WindowsAppNotificationSink.ParseActivation(arguments);

        Assert.Equal("ack", activation.Action);
        Assert.Equal("f1", activation.SourceFileId);
        Assert.Equal("k1", activation.LogicalRowKey);
        Assert.Equal("v1", activation.DeadlineVersion);
        Assert.Equal(AlertGroup.Overdue, activation.AlertGroup);
    }
}
