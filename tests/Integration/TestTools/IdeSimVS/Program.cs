// VS-style IDE simulator: mimics the real Visual Studio window lifecycle.
// Usage: IdeSimVS.exe [--sln] [folder-path]
//
// Two modes matching real VS behavior observed via process inspection:
//
// FOLDER MODE (default):
// 1. T+0s: Process starts, no visible windows
// 2. T+1s: Splash screen appears (HWND #1)
// 3. T+2s: Splash destroyed, main window created (HWND #2, generic title)
// 4. T+3s: Main window title updates to include project info
//
// SLN MODE (--sln flag):
// 1. T+0s: Process starts, no visible windows
// 2. T+1s: Splash screen appears (HWND #1)
// 3. T+2s: Splash destroyed, main window created (HWND #2, project title immediately)
//
// Window titles are intentionally random/unrelated to the folder path,
// to ensure no tracking logic depends on window titles.
// The PID stays the same throughout — VS does NOT use a launcher process.

using System;
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

// Phase 2: Main window
var main = new Form
{
    // SLN mode: project title immediately. Folder mode: generic title first.
    Text = slnMode
        ? $"RandomProject.sln - IDE Simulator {Guid.NewGuid():N}"
        : "IDE Simulator",
    Width = 800,
    Height = 600,
    StartPosition = FormStartPosition.CenterScreen
};
main.Show();
Application.DoEvents();

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

Application.Run(main);
