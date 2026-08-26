using TaskTracker.Application;
using TaskTracker.Domain;
using Xunit;

namespace TaskTracker.Application.Tests;

public class NotificationActivationParserTests
{
    [Theory]
    [InlineData("action=ack&sourceFileId=f1&logicalRowKey=k1&deadlineVersion=v1&alertGroup=Overdue")]
    [InlineData("action=ack;sourceFileId=f1;logicalRowKey=k1;deadlineVersion=v1;alertGroup=Overdue")]
    public void Parse_HandlesSupportedSeparators(string arguments)
    {
        var activation = NotificationActivationParser.Parse(arguments);

        Assert.Equal("ack", activation.Action);
        Assert.Equal("f1", activation.SourceFileId);
        Assert.Equal("k1", activation.LogicalRowKey);
        Assert.Equal("v1", activation.DeadlineVersion);
        Assert.Equal(AlertGroup.Overdue, activation.AlertGroup);
    }
}
