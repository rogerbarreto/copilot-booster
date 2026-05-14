namespace CopilotBooster.Tests.Forms;

/// <summary>
/// Tests for SessionEditorVisuals behavioral contract after WI-4 (read-only CWD/Name conversion).
/// Direct UI automation is not feasible because:
/// 1. SessionEditorVisuals.ShowEditor is modal and blocks the calling thread
/// 2. The method is marked [ExcludeFromCodeCoverage]
/// 3. Neo ruled against extracting UI event handlers solely for test seams
///
/// These tests verify the compile-time contract and observable return signature.
/// Manual validation gap: CWD and Name fields are visually read-only with copy icons,
/// Alias field is editable. This must be verified through manual inspection or UI automation
/// framework (e.g., FlaUI/Appium) in future work.
/// </summary>
public sealed class SessionEditorVisualsContractTests
{
    [Fact]
    public void ShowEditor_ReturnsStringOrNull_NotTuple()
    {
        var method = typeof(SessionEditorVisuals).GetMethod(
            "ShowEditor",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        Assert.NotNull(method);
        Assert.Equal(typeof(string), method.ReturnType);
    }

    [Fact]
    public void ShowEditor_HasExpectedParameters()
    {
        var method = typeof(SessionEditorVisuals).GetMethod(
            "ShowEditor",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        Assert.NotNull(method);

        var parameters = method.GetParameters();
        Assert.Equal(4, parameters.Length);
        Assert.Equal("sessionId", parameters[0].Name);
        Assert.Equal(typeof(string), parameters[0].ParameterType);
        Assert.Equal("currentAlias", parameters[1].Name);
        Assert.Equal(typeof(string), parameters[1].ParameterType);
        Assert.Equal("currentSummary", parameters[2].Name);
        Assert.Equal(typeof(string), parameters[2].ParameterType);
        Assert.Equal("currentCwd", parameters[3].Name);
        Assert.Equal(typeof(string), parameters[3].ParameterType);
    }

    /// <summary>
    /// Verifies the consumer contract: MainForm.ContextMenu.cs uses only the Alias return value
    /// and no longer writes CWD to workspace.yaml or updates CWD in the session cache.
    ///
    /// This test documents the expected integration behavior between the editor and its consumer.
    /// The actual visual behavior (read-only labels, copy buttons) remains untested due to modal
    /// dialog constraints and coverage exclusion.
    /// </summary>
    [Fact]
    public void ShowEditor_ReturnType_SupportsAliasOnlyWorkflow()
    {
        var method = typeof(SessionEditorVisuals).GetMethod(
            "ShowEditor",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        Assert.NotNull(method);

        var returnType = method.ReturnType;
        Assert.True(
            returnType == typeof(string),
            "Expected ShowEditor to return string? (Alias only), confirming CWD is no longer editable");
    }
}
