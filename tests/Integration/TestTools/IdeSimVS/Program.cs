// VS-style IDE simulator: mimics the real Visual Studio window lifecycle.
// Usage: IdeSimVS.exe [--sln] [folder-path]
//
// Two modes matching real VS behavior observed via process inspection:
//
// FOLDER MODE (default):
// 1. T+0s: Process starts, no visible windows
// 2. T+1s: Splash screen appears (HWND #1)
// 3. T+2s: Splash destroyed, main window created (HWND #2, generic title)
//          + multiple tool/panel windows (HWNDs #3..#N) — like real VS
// 4. T+3s: Main window title updates to include project info
//
// SLN MODE (--sln flag):
// Same as folder mode but main window gets project title immediately.
//
// When killed, VS destroys ALL windows (main + tool windows) in sequence.
// This creates a cascade of EVENT_OBJECT_DESTROY events — the tracking
// system must not recapture to dying child windows.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

bool slnMode = args.Contains("--sln", StringComparer.OrdinalIgnoreCase);

// Phase 1: Splash screen (short-lived window) — different HWND than main
var splash = new Form
{
    Text = $"IDE Sim Enterprise {DateTime.Now.Ticks % 10000}",
    Width = 300,
    Height = 100,
    StartPosition = FormStartPosition.CenterScreen,
    FormBorderStyle = FormBorderStyle.FixedToolWindow,
    ShowInTaskbar = false
};
splash.Show();
Application.DoEvents();
Thread.Sleep(1500);
splash.Close();
splash.Dispose();

// Phase 2: Main window + independent tool windows (simulates VS panels)
// Tool windows are NOT owned by main — they're independent top-level windows.
// This matches real VS where the main window is destroyed FIRST,
// then tool windows are destroyed while the process is still alive.
var main = new Form
{
    Text = slnMode
        ? $"RandomProject.sln - IDE Simulator {Guid.NewGuid():N}"
        : "IDE Simulator",
    Width = 800,
    Height = 600,
    StartPosition = FormStartPosition.CenterScreen
};

var toolWindows = new List<Form>();
for (int i = 0; i < 10; i++)
{
    var tool = new Form
    {
        Text = $"Tool Window {i} - {Guid.NewGuid():N}",
        Width = 200,
        Height = 150,
        StartPosition = FormStartPosition.Manual,
        Left = 50 + (i * 20),
        Top = 50 + (i * 20),
        ShowInTaskbar = true
    };
    toolWindows.Add(tool);
}

main.Show();
foreach (var tool in toolWindows)
{
    tool.Show();
}
Application.DoEvents();

// On WM_CLOSE of the main window: do NOT auto-close tool windows.
// Let them stay alive (visible, separate HWNDs) while the process continues.
// This exactly matches real VS behavior: main window destroyed first,
// tool windows remain visible, process alive — causing cascading recapture.
// The process only exits when ALL windows are closed.
int openWindowCount = toolWindows.Count + 1; // +1 for main

main.FormClosed += (s, e) =>
{
    openWindowCount--;
    if (openWindowCount <= 0)
    {
        Application.ExitThread();
    }
};

foreach (var tool in toolWindows)
{
    tool.FormClosed += (s, e) =>
    {
        openWindowCount--;
        if (openWindowCount <= 0)
        {
            Application.ExitThread();
        }
    };
}

// Phase 3 (folder mode only): Title updates after a delay
if (!slnMode)
{
    var titleTimer = new System.Windows.Forms.Timer { Interval = 1000 };
    titleTimer.Tick += (s, e) =>
    {
        titleTimer.Stop();
        main.Text = $"SomeProject - IDE Simulator {Guid.NewGuid():N}";
    };
    titleTimer.Start();
}

Application.Run();
