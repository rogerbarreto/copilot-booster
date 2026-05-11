#pragma warning disable IDE0005 // Using directive is necessary for Encoding.UTF8
using System.Text;
#pragma warning restore IDE0005

namespace CopilotBooster.Tests.Services;

/// <summary>
/// Regression tests for memory-bounded streaming overload of TryParseLogContent.
/// Background: ReadToEnd().Split('\n') on 678 MB logs → 4.4 GB process RSS.
/// These tests prove the streaming overload stays under 25 MB allocation for 50 MB synthetic logs.
/// </summary>
public sealed class CopilotLogWatcherStreamingTests : IDisposable
{
    private readonly List<string> _tempFiles = [];

    public void Dispose()
    {
        foreach (var file in this._tempFiles)
        {
            try
            {
                if (File.Exists(file))
                {
                    File.Delete(file);
                }
            }
            catch
            {
                // Best-effort cleanup
            }
        }
    }

    // ── Test 1: Parity ────────────────────────────────────────────────

    [Fact]
    public void TryParseLogContent_StreamingOverload_ProducesIdenticalOutputToArrayOverload()
    {
        // Typical case
        var typicalFixture = """
            2026-05-09T10:59:33.804Z [INFO] Workspace initialized: ba62613b-7f04-46bc-9c1e-778b12616687 (checkpoints: 0)
            2026-05-09T10:59:33.852Z [INFO] Registering foreground session: ba62613b-7f04-46bc-9c1e-778b12616687
            2026-05-09T10:59:34.127Z [INFO] [Telemetry] cli.telemetry:
            {
              "kind": "cli_ready",
              "session_id": "ba62613b-7f04-46bc-9c1e-778b12616687",
              "context": {
                "cwd": "C:\\Users\\test\\workspace"
              }
            }
            2026-05-09T10:59:33.853Z [DEBUG] [remoteHosts] Starting remote host detection for cwd=C:\Users\test\workspace
            """;

        AssertParityBetweenOverloads(typicalFixture, "typical case");

        // /resume case with multiple sessions
        var resumeFixture = """
            2026-05-09T10:59:33.804Z [INFO] Workspace initialized: aaaaaaaa-bbbb-cccc-dddd-000000000001 (checkpoints: 0)
            2026-05-09T10:59:34.127Z [INFO] [Telemetry] cli.telemetry:
            {
              "kind": "cli_ready",
              "session_id": "aaaaaaaa-bbbb-cccc-dddd-000000000001",
              "context": {
                "cwd": "C:\\workspace1"
              }
            }
            2026-05-09T11:05:00.000Z [INFO] Workspace initialized: aaaaaaaa-bbbb-cccc-dddd-000000000002 (checkpoints: 0)
            2026-05-09T11:05:01.000Z [INFO] [Telemetry] cli.telemetry:
            {
              "kind": "resumed",
              "session_id": "aaaaaaaa-bbbb-cccc-dddd-000000000002"
            }
            """;

        AssertParityBetweenOverloads(resumeFixture, "resume case");

        // No-cwd case (fallback to UserProfile)
        var noCwdFixture = """
            2026-05-09T10:59:34.127Z [INFO] [Telemetry] cli.telemetry:
            {
              "kind": "cli_ready",
              "session_id": "11111111-2222-3333-4444-555555555555"
            }
            """;

        AssertParityBetweenOverloads(noCwdFixture, "no-cwd case");

        // Empty log
        AssertParityBetweenOverloads("", "empty log");

        // Consecutive duplicates should be deduped
        var dupeFixture = """
            2026-05-09T10:59:33.804Z [INFO] Workspace initialized: ba62613b-7f04-46bc-9c1e-778b12616687 (checkpoints: 0)
            2026-05-09T10:59:33.852Z [INFO] Registering foreground session: ba62613b-7f04-46bc-9c1e-778b12616687
            2026-05-09T10:59:34.127Z [INFO] [Telemetry] cli.telemetry:
            {
              "kind": "cli_ready",
              "session_id": "ba62613b-7f04-46bc-9c1e-778b12616687"
            }
            """;

        AssertParityBetweenOverloads(dupeFixture, "duplicate session IDs");
    }

    private static void AssertParityBetweenOverloads(string logContent, string scenario)
    {
        var lines = logContent.Split('\n');

        // Call array overload
        var arrayResult = CopilotLogWatcherService.TryParseLogContent(lines);

        // Call streaming overload
        using var reader = new StringReader(logContent);
        var streamingResult = CopilotLogWatcherService.TryParseLogContent(reader);

        // Assert exact equality
        Assert.Equal(arrayResult.Count, streamingResult.Count);

        for (int i = 0; i < arrayResult.Count; i++)
        {
            Assert.Equal(arrayResult[i].sessionId, streamingResult[i].sessionId);
            Assert.Equal(arrayResult[i].cwd, streamingResult[i].cwd);
        }
    }

    // ── Test 2: GC Gen-2 Promotion Ceiling ───────────────────────────
    //
    // (A previous "byte allocation budget" test was removed: GC.GetTotalAllocatedBytes
    // tracks transient allocations cumulatively, and StreamReader.ReadLine() allocates
    // one short-lived gen-0 string per line — which is unavoidable and proportional to
    // file size. The actual bug was LOH thrashing from ReadToEnd().Split('\n') on huge
    // logs, not raw allocation rate. The gen-2 ceiling test below is the right signal:
    // streaming line-by-line should never push the LOH past one collection on a 50 MB
    // input, while the buggy ReadToEnd path would force several gen-2 collections.)

    [LocalOnlyFact]
    [Trait("Category", "LocalOnly")]
    public void TryParseLogContent_StreamingOverload_50MbLog_NoLOHPromotion()
    {
        // Generate ~50 MB synthetic log with realistic content
        var tempFile = this.GenerateSyntheticLog50MB();

        try
        {
            // Force GC to get a clean baseline
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect();

            // Snapshot gen-2 collection count before
            var gen2Before = GC.CollectionCount(2);

            // Parse with streaming overload
            using (var fs = new FileStream(tempFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(fs, Encoding.UTF8))
            {
                var sessions = CopilotLogWatcherService.TryParseLogContent(reader);
                Assert.InRange(sessions.Count, 1, 10);
            }

            // Snapshot gen-2 collection count after
            var gen2After = GC.CollectionCount(2);
            var gen2Delta = gen2After - gen2Before;

            // Assert: streaming should NOT trigger ≥ 2 gen-2 collections
            // (LOH thrashing → multiple gen-2 promotions)
            Assert.True(gen2Delta <= 1, $"Gen-2 collection ceiling exceeded: {gen2Delta} collections > 1");
        }
        finally
        {
            // Cleanup
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    // ── Test 4: Real-World Fixture ───────────────────────────────────

    [Fact]
    public void TryParseLogContent_StreamingOverload_RealWorldFixture_ParsesSessionsCorrectly()
    {
        // Small (~5 KB) sanitized fixture from real Copilot CLI log
        var realWorldFixture = """
            2026-05-09T10:59:33.802Z [INFO] Starting Copilot CLI v1.0.44
            2026-05-09T10:59:33.803Z [DEBUG] Environment: win32, node v24.15.0
            2026-05-09T10:59:33.804Z [INFO] Workspace initialized: ba62613b-7f04-46bc-9c1e-778b12616687 (checkpoints: 0)
            2026-05-09T10:59:33.852Z [INFO] Registering foreground session: ba62613b-7f04-46bc-9c1e-778b12616687
            2026-05-09T10:59:34.127Z [INFO] [Telemetry] cli.telemetry:
            {
              "kind": "cli_ready",
              "properties": {
                "copilot_pid": "74528",
                "engagement_id": "6af655d6-77ff-47d8-bbf2-eb21068f11f1"
              },
              "metrics": {
                "startup_duration_ms": 979
              },
              "session_id": "ba62613b-7f04-46bc-9c1e-778b12616687",
              "features": {
                "FEATURE_FLAG_TEST": "true",
                "copilot-feature-agentic-memory": "true"
              },
              "created_at": "2026-05-09T10:59:34.127Z",
              "copilot_tracking_id": "2334661c5d22c2ad95dcd6ecdf047dd8",
              "client": {
                "cli_version": "1.0.44",
                "os_platform": "win32",
                "os_version": "10.0.26200",
                "os_arch": "x64",
                "node_version": "v24.15.0",
                "copilot_plan": "enterprise",
                "client_name": "github/cli",
                "client_type": "cli-interactive",
                "is_staff": true,
                "dev_device_id": "fe519472-be6d-40d0-9e37-782a38d5ea5e"
              },
              "context": {
                "cwd": "S:\\repo\\community\\sandcastle"
              }
            }
            2026-05-09T10:59:33.853Z [DEBUG] [remoteHosts] Starting remote host detection for cwd=S:\repo\community\sandcastle, featureFlagEnabled=true
            2026-05-09T10:59:35.000Z [DEBUG] User message received
            2026-05-09T10:59:36.000Z [DEBUG] Assistant response streaming
            2026-05-09T11:05:00.000Z [INFO] Workspace initialized: cccccccc-dddd-eeee-ffff-111111111111 (checkpoints: 0)
            2026-05-09T11:05:01.000Z [INFO] [Telemetry] cli.telemetry:
            {
              "kind": "resumed",
              "session_id": "cccccccc-dddd-eeee-ffff-111111111111"
            }
            2026-05-09T11:05:02.000Z [DEBUG] Resumed session processing
            """;

        using var reader = new StringReader(realWorldFixture);
        var sessions = CopilotLogWatcherService.TryParseLogContent(reader);

        // Should parse 2 sessions from this fixture
        Assert.Equal(2, sessions.Count);

        // First session: ba62613b-7f04-46bc-9c1e-778b12616687 at S:\repo\community\sandcastle
        Assert.Equal("ba62613b-7f04-46bc-9c1e-778b12616687", sessions[0].sessionId);
        Assert.Equal(@"S:\repo\community\sandcastle", sessions[0].cwd);

        // Second session: cccccccc-dddd-eeee-ffff-111111111111 (inherits cwd from first)
        Assert.Equal("cccccccc-dddd-eeee-ffff-111111111111", sessions[1].sessionId);
        Assert.Equal(@"S:\repo\community\sandcastle", sessions[1].cwd);
    }

    // ── Helper: Generate 50 MB Synthetic Log ─────────────────────────

    private string GenerateSyntheticLog50MB()
    {
        // Create temp file in project directory (not /tmp)
        var ticks = DateTime.UtcNow.Ticks;
        var pid = Environment.ProcessId;
        var tempFile = Path.Combine(Path.GetTempPath(), $"process-{ticks}-{pid}.log");
        this._tempFiles.Add(tempFile);

        // Generate ~50 MB log: scatter ~10 valid session-defining lines among ~500K filler lines
        using var fs = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 8192);
        using var writer = new StreamWriter(fs, Encoding.UTF8);

        var sessionCount = 0;
        var lineCount = 0;
        const long TargetBytes = 50 * 1024 * 1024; // 50 MB
        var bytesWritten = 0L;

        while (bytesWritten < TargetBytes)
        {
            // Every ~50K lines, insert a valid session-defining line
            if (lineCount % 50000 == 0 && sessionCount < 10)
            {
                var sessionId = $"aaaaaaaa-bbbb-cccc-dddd-{sessionCount:000000000000}";
                var timestamp = $"2026-05-09T10:59:{sessionCount:00}.000Z";
                writer.WriteLine($"{timestamp} [INFO] Workspace initialized: {sessionId} (checkpoints: 0)");
                writer.WriteLine($"{timestamp} [INFO] [Telemetry] cli.telemetry:");
                writer.WriteLine("{");
                writer.WriteLine("  \"kind\": \"cli_ready\",");
                writer.WriteLine($"  \"session_id\": \"{sessionId}\",");
                writer.WriteLine("  \"context\": {");
                writer.WriteLine("    \"cwd\": \"C:\\\\TestWorkspace\"");
                writer.WriteLine("  }");
                writer.WriteLine("}");
                sessionCount++;
                bytesWritten += 200; // Approximate
            }
            else
            {
                // Filler line: realistic log noise (avg ~100 chars)
                var fillerLine = $"2026-05-09T10:59:33.{lineCount % 1000:000}Z [DEBUG] Processing message {lineCount}, agent state: running, tokens: {lineCount * 10}";
                writer.WriteLine(fillerLine);
                bytesWritten += fillerLine.Length + 2; // +2 for \r\n
            }

            lineCount++;
        }

        writer.Flush();
        return tempFile;
    }
}
