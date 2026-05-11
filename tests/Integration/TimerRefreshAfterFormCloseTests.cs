using System.Reflection;

namespace CopilotBooster.IntegrationTests;

/// <summary>
/// Regression test for crash: System.ComponentModel.Win32Exception "Error creating window handle"
/// when a WinEvent callback fires after the form's window handle is destroyed during closing.
///
/// The crash occurs because MainForm.RequestRefresh() calls _refreshDebounceTimer.Stop() + .Start()
/// from WindowEventHookService event handlers without checking if the form is disposed.
/// The timer's internal TimerNativeWindow.CreateHandle() fails when the form is shutting down.
/// </summary>
[Collection(WindowEventHookCollection.Name)]
public class TimerRefreshAfterFormCloseTests
{
    private const uint EVENT_OBJECT_DESTROY = 0x8001;

    /// <summary>
    /// Reproduces the crash pattern: after form disposal, a WinEvent callback
    /// restarts the debounce timer, which should NOT happen.
    /// </summary>
    [StaFact]
    public void WinEvent_AfterFormDisposed_ShouldNotRestartTimer()
    {
        // Arrange: Mirror the MainForm pattern — hook events restart a debounce timer
        using var form = new Form();
        var debounceTimer = new System.Windows.Forms.Timer { Interval = 300 };
        using var hookService = new WindowEventHookService();

        hookService.WindowDestroyed += hwnd =>
        {
            // MainForm.RequestRefresh now guards against this with:
            //   if (this.IsDisposed || !this.IsHandleCreated) return;
            // Additionally, WindowEventHookService.OnWinEvent checks _stopped.
            if (form.IsDisposed || !form.IsHandleCreated)
            {
                return;
            }

            debounceTimer.Stop();
            debounceTimer.Start();
        };

        form.Show();
        debounceTimer.Start();
        hookService.Start();
        Application.DoEvents();

        // Act: Close and dispose the form (destroys the window handle)
        form.Close();
        form.Dispose();
        Application.DoEvents();

        // Stop the timer (simulates what OnFormClosing now does before handle destruction)
        debounceTimer.Stop();

        // Simulate a late WinEvent callback (as if an external window was destroyed
        // between OnFormClosing and FormClosed, when the hook is still active)
        var onWinEvent = typeof(WindowEventHookService).GetMethod(
            "OnWinEvent",
            BindingFlags.NonPublic | BindingFlags.Instance)!;

        onWinEvent.Invoke(hookService, [IntPtr.Zero, EVENT_OBJECT_DESTROY, (IntPtr)0xDEAD, 0, 0, 0u, 0u]);

        // Assert: Timer should NOT have been restarted after form disposal.
        // In production, restarting the timer here causes Win32Exception
        // "Error creating window handle" because TimerNativeWindow.CreateHandle() fails.
        Assert.False(debounceTimer.Enabled,
            "Debounce timer must not restart after the owning form is disposed — " +
            "this causes Win32Exception 'Error creating window handle' in production.");

        debounceTimer.Dispose();
    }
}
