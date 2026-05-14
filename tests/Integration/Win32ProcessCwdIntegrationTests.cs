using System.Diagnostics;

namespace CopilotBooster.IntegrationTests.Integration;

public sealed class Win32ProcessCwdIntegrationTests
{
    [LocalOnlyFact]
    public void Get_WithSpawnedProcess_ReturnsProcessCwd()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"cwd-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);

        Process? process = null;
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c pause",
                WorkingDirectory = tempDir,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            process = Process.Start(startInfo);
            Assert.NotNull(process);

            Thread.Sleep(500);

            var result = Win32ProcessCwd.Get(process.Id);

            Assert.Equal(tempDir, result);
        }
        finally
        {
            if (process != null && !process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
                process.Dispose();
            }

            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void Get_WithNonExistentProcess_ReturnsNull()
    {
        var result = Win32ProcessCwd.Get(int.MaxValue);

        Assert.Null(result);
    }

    [Fact]
    public void Get_WithInvalidPid_ReturnsNull()
    {
        var result = Win32ProcessCwd.Get(-1);

        Assert.Null(result);
    }
}
