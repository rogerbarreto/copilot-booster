using System.Text;

namespace CopilotBooster.Tests.Services;

public sealed class CopilotLogTailReadTests
{
    private const int DefaultTailBytes = 256 * 1024;
    private const string SessionA = "aaaaaaaa-bbbb-cccc-dddd-aaaaaaaaaaaa";
    private const string SessionB = "bbbbbbbb-cccc-dddd-eeee-bbbbbbbbbbbb";
    private const string PartialBoundarySession = "cccccccc-dddd-eeee-ffff-cccccccccccc";
    private const string TailTargetSession = "dddddddd-eeee-ffff-aaaa-dddddddddddd";

    [Fact]
    public void TryParseLogTail_SmallFile_ReturnsAllSessions()
    {
        var cwd = @"C:\repo\small-tail";
        var logContent = string.Concat(
            TelemetryBlock(SessionA, cwd),
            "2026-05-10T11:00:01.000Z [DBG] filler\n",
            TelemetryBlock(SessionB, cwd),
            "2026-05-10T11:00:02.000Z [DBG] filler\n",
            TelemetryBlock(SessionA, cwd));
        var path = WriteTempLog(logContent);

        try
        {
            var sessions = CopilotLogWatcherService.TryParseLogTail(path);

            Assert.Equal(3, sessions.Count);
            Assert.Equal(SessionA, sessions[0].sessionId);
            Assert.Equal(cwd, sessions[0].cwd);
            Assert.Equal(SessionB, sessions[1].sessionId);
            Assert.Equal(cwd, sessions[1].cwd);
            Assert.Equal(SessionA, sessions[2].sessionId);
            Assert.Equal(cwd, sessions[2].cwd);
        }
        finally
        {
            DeleteTempLog(path);
        }
    }

    [Fact]
    public void TryParseLogTail_LargeFile_ReturnsLatestSessionAtTail()
    {
        var cwd = @"C:\repo\large-tail";
        var builder = new StringBuilder();

        while (builder.Length < 430 * 1024)
        {
            builder.Append(TelemetryBlock(SessionA, cwd));
            builder.Append("2026-05-10T11:00:00.000Z [DBG] upstream filler ");
            builder.Append('x', 512);
            builder.Append('\n');
        }

        builder.Append('y', 90 * 1024);
        builder.Append('\n');
        builder.Append(TelemetryBlock(SessionB, cwd));
        builder.Append("2026-05-10T11:30:00.000Z [DBG] tail filler\n");

        var path = WriteTempLog(builder.ToString());

        try
        {
            Assert.True(new FileInfo(path).Length > 512 * 1024);

            var sessions = CopilotLogWatcherService.TryParseLogTail(path, maxTailBytes: DefaultTailBytes);

            Assert.NotEmpty(sessions);
            Assert.Equal(SessionB, sessions[^1].sessionId);
            Assert.Equal(cwd, sessions[^1].cwd);
        }
        finally
        {
            DeleteTempLog(path);
        }
    }

    [Fact]
    public void TryParseLogTail_TailAlignsToNewline_NoCorruptedBlock()
    {
        var cwd = @"C:\repo\boundary-tail";
        var prefix = string.Concat(Enumerable.Repeat("2026-05-10T11:00:00.000Z [DBG] prefix filler\n", 80));
        var partialBoundaryBlock = TelemetryBlock(PartialBoundarySession, cwd);
        var targetBlock = TelemetryBlock(TailTargetSession, cwd);
        var logContent = prefix + partialBoundaryBlock + targetBlock;

        var boundaryOffset = prefix.Length + partialBoundaryBlock.IndexOf("\"session_id\"", StringComparison.Ordinal);
        var maxTailBytes = logContent.Length - boundaryOffset;
        var path = WriteTempLog(logContent);

        try
        {
            Assert.InRange(maxTailBytes, targetBlock.Length + 1, logContent.Length - 1);

            var sessions = CopilotLogWatcherService.TryParseLogTail(path, maxTailBytes);

            var sessionIds = sessions.Select(session => session.sessionId).ToArray();
            Assert.DoesNotContain(PartialBoundarySession, sessionIds);
            var session = Assert.Single(sessions);
            Assert.Equal(TailTargetSession, session.sessionId);
            Assert.Equal(cwd, session.cwd);
        }
        finally
        {
            DeleteTempLog(path);
        }
    }

    [Fact]
    public void TryParseLogTail_NonExistentFile_ReturnsEmpty()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.log");

        var sessions = CopilotLogWatcherService.TryParseLogTail(path);

        Assert.Empty(sessions);
    }

    private static string TelemetryBlock(string sessionId, string cwd)
    {
        var escapedCwd = cwd.Replace(@"\", @"\\", StringComparison.Ordinal);
        var builder = new StringBuilder();
        builder.AppendLine("2026-05-10T11:00:00.000Z [INFO] [Telemetry] cli.telemetry:");
        builder.AppendLine("{");
        builder.AppendLine("  \"event\": \"user_prompt\",");
        builder.Append("  \"session_id\": \"");
        builder.Append(sessionId);
        builder.AppendLine("\",");
        builder.AppendLine("  \"context\": {");
        builder.Append("    \"cwd\": \"");
        builder.Append(escapedCwd);
        builder.AppendLine("\"");
        builder.AppendLine("  }");
        builder.AppendLine("}");
        return builder.ToString();
    }

    private static string WriteTempLog(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.log");
        File.WriteAllText(path, content);
        return path;
    }

    private static void DeleteTempLog(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
        }
    }
}
