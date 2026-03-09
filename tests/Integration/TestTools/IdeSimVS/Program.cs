// VS-style IDE simulator: launcher creates a splash window, destroys it,
// then creates the real main window — all under the SAME PID.
// Usage: IdeSimVS.exe [folder-path]
// The folder name appears in the main window title (like "MyProject - Visual Studio").

using System;
using System.Threading;
using System.Windows.Forms;

var folderName = args.Length > 0
    ? System.IO.Path.GetFileName(args[0].TrimEnd('\\'))
    : "Untitled";

// Phase 1: Splash screen (short-lived window)
var splash = new Form
{
    Text = "IDE Simulator Loading...",
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

// Phase 2: Main IDE window (long-lived)
var main = new Form
{
    Text = $"{folderName} - IDE Simulator",
    Width = 800,
    Height = 600,
    StartPosition = FormStartPosition.CenterScreen
};

Application.Run(main);
