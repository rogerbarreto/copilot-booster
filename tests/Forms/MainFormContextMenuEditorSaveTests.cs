namespace CopilotBooster.Tests.Forms;

/// <summary>
/// Neo Q3 accepted the direct WinForms handler test gap, so these source contract tests pin
/// the editor save consumer behavior without extracting a production seam solely for tests.
/// </summary>
public sealed class MainFormContextMenuEditorSaveTests
{
    [Fact]
    public void EditorSave_DoesNotCallUpdateSessionCwd()
    {
        var source = ReadMainFormContextMenuSource();

        Assert.DoesNotContain("SessionService.UpdateSessionCwd", source, StringComparison.Ordinal);
    }

    [Fact]
    public void EditorSave_DoesNotMutateSessionCwdInMemory()
    {
        var source = ReadMainFormContextMenuSource();

        Assert.DoesNotContain("session.Cwd =", source, StringComparison.Ordinal);
        Assert.DoesNotContain("session.Folder =", source, StringComparison.Ordinal);
    }

    [Fact]
    public void EditorSave_DoesNotMutateGridCwdCell()
    {
        var source = ReadMainFormContextMenuSource();

        Assert.DoesNotContain("row.Cells[\"CWD\"].Value", source, StringComparison.Ordinal);
    }

    [Fact]
    public void EditorSave_StillCallsSetAliasWhenAliasChanged()
    {
        var source = ReadMainFormContextMenuSource();

        Assert.Contains("SessionAliasService.SetAlias", source, StringComparison.Ordinal);
    }

    private static string ReadMainFormContextMenuSource()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, "src", "Forms", "MainForm.ContextMenu.cs");
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not find src\\Forms\\MainForm.ContextMenu.cs from test output directory.");
    }
}
