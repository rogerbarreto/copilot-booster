using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace CopilotBooster.Services;

/// <summary>
/// Retrieves the current working directory of a running process by reading its PEB (Process Environment Block).
/// Supports 64-bit processes only. Copilot CLI is always 64-bit on Windows.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class Win32ProcessCwd
{
    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    private const uint PROCESS_VM_READ = 0x0010;
    private const int ProcessBasicInformation = 0;

    // 64-bit PEB offsets
    private const int PEB_PROCESS_PARAMETERS_OFFSET = 0x20;
    private const int RTL_CURRENT_DIRECTORY_OFFSET = 0x38;

    // Cache: (pid, processStartTime) → cwd or null
    private static readonly ConcurrentDictionary<(int, DateTime), string?> s_cache = new();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(
        SafeProcessHandle processHandle,
        int processInformationClass,
        out PROCESS_BASIC_INFORMATION processInformation,
        int processInformationLength,
        out int returnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadProcessMemory(
        SafeProcessHandle hProcess,
        IntPtr lpBaseAddress,
        byte[] lpBuffer,
        int dwSize,
        out int lpNumberOfBytesRead);

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_BASIC_INFORMATION
    {
        public IntPtr Reserved1;
        public IntPtr PebBaseAddress;
        public IntPtr Reserved2_0;
        public IntPtr Reserved2_1;
        public IntPtr UniqueProcessId;
        public IntPtr Reserved3;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UNICODE_STRING
    {
        public ushort Length;
        public ushort MaximumLength;
        public IntPtr Buffer;
    }

    /// <summary>
    /// Gets the current working directory of the specified process.
    /// </summary>
    /// <param name="pid">Process ID.</param>
    /// <returns>The current working directory path, or null if it cannot be determined.</returns>
    internal static string? Get(int pid)
    {
        try
        {
            // Get process start time for cache key
            DateTime startTime;
            try
            {
                using var proc = Process.GetProcessById(pid);
                startTime = proc.StartTime;
            }
            catch
            {
                // Process not found or access denied
                return null;
            }

            // Check cache
            var cacheKey = (pid, startTime);
            if (s_cache.TryGetValue(cacheKey, out var cachedCwd))
            {
                return cachedCwd;
            }

            // Perform PEB probe
            var cwd = ProbeProcessCwd(pid);

            // Cache result (even if null)
            s_cache[cacheKey] = cwd;

            return cwd;
        }
        catch
        {
            // Never throw to callers
            return null;
        }
    }

    private static string? ProbeProcessCwd(int pid)
    {
        try
        {
            // Open process with query and read permissions
            using var handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION | PROCESS_VM_READ, false, pid);
            if (handle.IsInvalid)
            {
                return null;
            }

            // Query for PEB address
            var status = NtQueryInformationProcess(
                handle,
                ProcessBasicInformation,
                out var pbi,
                Marshal.SizeOf<PROCESS_BASIC_INFORMATION>(),
                out _);

            if (status != 0 || pbi.PebBaseAddress == IntPtr.Zero)
            {
                return null;
            }

            // Read ProcessParameters pointer from PEB
            var processParamsPtr = ReadPointer(handle, pbi.PebBaseAddress + PEB_PROCESS_PARAMETERS_OFFSET);
            if (processParamsPtr == IntPtr.Zero)
            {
                return null;
            }

            // Read UNICODE_STRING CurrentDirectory.DosPath from RTL_USER_PROCESS_PARAMETERS
            var unicodeString = ReadUnicodeString(handle, processParamsPtr + RTL_CURRENT_DIRECTORY_OFFSET);
            if (!unicodeString.HasValue)
            {
                return null;
            }

            var us = unicodeString.Value;

            // Validate UNICODE_STRING fields
            if (us.Length == 0 || us.Length > us.MaximumLength || us.Length % 2 != 0 || us.Buffer == IntPtr.Zero)
            {
                return null;
            }

            // Read the actual string bytes
            var buffer = new byte[us.Length];
            if (!ReadProcessMemory(handle, us.Buffer, buffer, us.Length, out var bytesRead) || bytesRead != us.Length)
            {
                return null;
            }

            // Convert to string
            return Encoding.Unicode.GetString(buffer);
        }
        catch
        {
            return null;
        }
    }

    private static IntPtr ReadPointer(SafeProcessHandle handle, IntPtr address)
    {
        var buffer = new byte[IntPtr.Size];
        if (!ReadProcessMemory(handle, address, buffer, IntPtr.Size, out var bytesRead) || bytesRead != IntPtr.Size)
        {
            return IntPtr.Zero;
        }

        return IntPtr.Size == 8
            ? new IntPtr(BitConverter.ToInt64(buffer, 0))
            : new IntPtr(BitConverter.ToInt32(buffer, 0));
    }

    private static UNICODE_STRING? ReadUnicodeString(SafeProcessHandle handle, IntPtr address)
    {
        var size = Marshal.SizeOf<UNICODE_STRING>();
        var buffer = new byte[size];

        if (!ReadProcessMemory(handle, address, buffer, size, out var bytesRead) || bytesRead != size)
        {
            return null;
        }

        var length = BitConverter.ToUInt16(buffer, 0);
        var maxLength = BitConverter.ToUInt16(buffer, 2);
        var bufferPtr = IntPtr.Size == 8
            ? new IntPtr(BitConverter.ToInt64(buffer, 4))
            : new IntPtr(BitConverter.ToInt32(buffer, 4));

        return new UNICODE_STRING
        {
            Length = length,
            MaximumLength = maxLength,
            Buffer = bufferPtr
        };
    }
}
