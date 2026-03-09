// VS Code-style IDE simulator: a SINGLE-INSTANCE host process that owns ALL windows.
// The launcher (first run) starts the host; subsequent runs send a message to the
// existing host to open a new window, then exit.
//
// Usage: IdeSimVSCode.exe [folder-path]
// Each invocation either starts the host or tells the host to open a new window.
// Window title: "folder-name - IDE Code Simulator"
//
// Uses a named mutex + named pipe for single-instance coordination.

using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Windows.Forms;

const string MutexName = "IdeSimVSCode_SingleInstance_Mutex";
const string PipeName = "IdeSimVSCode_Pipe";

var folderPath = args.Length > 0 ? args[0] : "";
var folderName = string.IsNullOrEmpty(folderPath)
    ? "Untitled"
    : Path.GetFileName(folderPath.TrimEnd('\\'));

bool createdNew;
using var mutex = new Mutex(true, MutexName, out createdNew);

if (createdNew)
{
    // We are the HOST process — run the message loop and listen for new window requests
    var windows = new System.Collections.Generic.List<Form>();

    // Open first window
    var firstWindow = CreateWindow(folderName);
    windows.Add(firstWindow);
    firstWindow.Show();

    // Listen for new window requests on a background thread
    var listenerThread = new Thread(() =>
    {
        while (true)
        {
            try
            {
                using var server = new NamedPipeServerStream(PipeName, PipeDirection.In);
                server.WaitForConnection();
                using var reader = new StreamReader(server);
                var requestedFolder = reader.ReadLine() ?? "Untitled";

                // Marshal to UI thread to create the window
                firstWindow.BeginInvoke(() =>
                {
                    var newWindow = CreateWindow(requestedFolder);
                    windows.Add(newWindow);
                    newWindow.FormClosed += (s, e) =>
                    {
                        windows.Remove(newWindow);
                        if (windows.Count == 0)
                        {
                            Application.ExitThread();
                        }
                    };
                    newWindow.Show();
                });
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Pipe error: {ex.Message}");
                break;
            }
        }
    })
    { IsBackground = true };
    listenerThread.Start();

    // Run the message loop — don't exit when the first window closes.
    // Keep running as long as any window is open (like VS Code does).
    firstWindow.FormClosed += (s, e) =>
    {
        windows.Remove(firstWindow);
        if (windows.Count == 0)
        {
            Application.ExitThread();
        }
    };

    Application.Run();
}
else
{
    // Another instance is already running — send folder name to it and EXIT
    try
    {
        using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
        client.Connect(3000);
        using var writer = new StreamWriter(client) { AutoFlush = true };
        writer.WriteLine(folderName);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Failed to connect to host: {ex.Message}");
    }

    // EXIT immediately — launcher behavior
}

static Form CreateWindow(string folder)
{
    return new Form
    {
        Text = $"{folder} - IDE Code Simulator",
        Width = 800,
        Height = 600,
        StartPosition = FormStartPosition.CenterScreen
    };
}
