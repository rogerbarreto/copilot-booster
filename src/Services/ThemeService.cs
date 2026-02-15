using System.Diagnostics.CodeAnalysis;
using System.Windows.Forms;

namespace CopilotBooster.Services;

/// <summary>
/// Provides helpers for applying and converting UI theme values.
/// </summary>
internal static class ThemeService
{
    /// <summary>
    /// Applies the specified theme by setting the application color mode.
    /// </summary>
    /// <param name="theme">Theme name: <c>"light"</c>, <c>"dark"</c>, or <c>"system"</c>.</param>
    [ExcludeFromCodeCoverage]
    internal static void ApplyTheme(string theme)
    {
        var mode = theme switch
        {
            "light" => SystemColorMode.Classic,
            "dark" => SystemColorMode.Dark,
            _ => SystemColorMode.System,
        };

        Application.SetColorMode(mode);
    }

    /// <summary>
    /// Converts a theme name to a zero-based combo box index.
    /// </summary>
    /// <param name="theme">Theme name: <c>"light"</c>, <c>"dark"</c>, or <c>"system"</c>.</param>
    /// <returns>
    /// <c>1</c> for <c>"light"</c>, <c>2</c> for <c>"dark"</c>, or <c>0</c> for any other value.
    /// </returns>
    internal static int ThemeToIndex(string theme) => theme switch
    {
        "light" => 1,
        "dark" => 2,
        _ => 0,
    };

    /// <summary>
    /// Converts a zero-based combo box index to a theme name.
    /// </summary>
    /// <param name="index">The combo box index.</param>
    /// <returns>
    /// <c>"light"</c> for <c>1</c>, <c>"dark"</c> for <c>2</c>, or <c>"system"</c> for any other value.
    /// </returns>
    internal static string IndexToTheme(int index) => index switch
    {
        1 => "light",
        2 => "dark",
        _ => "system",
    };
}
