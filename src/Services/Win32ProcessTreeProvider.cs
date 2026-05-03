using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CopilotBooster.Services;

/// <summary>
/// Win32-based implementation of <see cref="IProcessTreeProvider"/> using Toolhelp32 snapshots.
/// </summary>
internal sealed class Win32ProcessTreeProvider : IProcessTreeProvider
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

    [DllImport("kernel32.dll")]
    private static extern bool Process32First(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

    [DllImport("kernel32.dll")]
    private static extern bool Process32Next(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    private const uint TH32CS_SNAPPROCESS = 0x00000002;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PROCESSENTRY32
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public IntPtr th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szExeFile;
    }

    public int? GetParentPid(int pid)
    {
        IntPtr snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
        if (snapshot == IntPtr.Zero || snapshot == new IntPtr(-1))
        {
            return null;
        }

        try
        {
            PROCESSENTRY32 entry = new() { dwSize = (uint)Marshal.SizeOf(typeof(PROCESSENTRY32)) };
            if (!Process32First(snapshot, ref entry))
            {
                return null;
            }

            do
            {
                if (entry.th32ProcessID == pid)
                {
                    return entry.th32ParentProcessID == 0 ? null : (int)entry.th32ParentProcessID;
                }
            }
            while (Process32Next(snapshot, ref entry));

            return null;
        }
        finally
        {
            CloseHandle(snapshot);
        }
    }

    public string? GetProcessName(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return process.ProcessName;
        }
        catch
        {
            return null;
        }
    }

    public IntPtr GetTopLevelWindow(int pid)
    {
        return WindowFocusService.FindWindowHandleByPid(pid);
    }
}
