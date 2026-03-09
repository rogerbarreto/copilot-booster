// VS-style IDE simulator: launcher creates a splash window, destroys it,
// then creates the real main window — all under the SAME PID.
// Usage: IdeSimVS.exe [folder-path]
// Window titles are intentionally random/unrelated to the folder name,
// matching how real VS shows "ProjectName.sln - Microsoft Visual Studio"
// where ProjectName differs from the folder path passed to Process.Start.

using System;
using System.Threading;
using System.Windows.Forms;

// Phase 1: Splash screen (short-lived window) — title has NO folder reference
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

// Phase 2: Main IDE window — title is random, NOT derived from the folder path.
// This ensures no tracking logic can rely on the window title matching the folder.
var main = new Form
{
    Text = $"SomeProject.sln - IDE Simulator {Guid.NewGuid():N}",
    Width = 800,
    Height = 600,
    StartPosition = FormStartPosition.CenterScreen
};

Application.Run(main);
