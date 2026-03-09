// VS-style IDE simulator: mimics the real Visual Studio window lifecycle.
// Usage: IdeSimVS.exe [folder-path]
//
// Real VS behavior (observed via process inspection):
// 1. T+0s: Process starts, no visible windows
// 2. T+1s: Splash screen appears (HWND #1, "IDE Sim Enterprise ...")
// 3. T+2s: Splash destroyed, main window created (HWND #2, generic title)
// 4. T+3s: Main window title updates (still HWND #2, now includes project info)
//
// Window titles are intentionally random/unrelated to the folder name,
// to ensure no tracking logic depends on window titles.
// The PID stays the same throughout — VS does NOT use a launcher process.

using System;
using System.Threading;
using System.Windows.Forms;

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

// Phase 2: Main window with generic title (no project info yet) — new HWND
var main = new Form
{
    Text = "IDE Simulator",
    Width = 800,
    Height = 600,
    StartPosition = FormStartPosition.CenterScreen
};
main.Show();
Application.DoEvents();

// Phase 3: Title updates after a delay (simulates VS loading the project)
var titleTimer = new System.Windows.Forms.Timer { Interval = 1000 };
titleTimer.Tick += (s, e) =>
{
    titleTimer.Stop();
    main.Text = $"SomeProject - IDE Simulator {Guid.NewGuid():N}";
};
titleTimer.Start();

Application.Run(main);
