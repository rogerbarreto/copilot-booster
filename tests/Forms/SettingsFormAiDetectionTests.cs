using System.Reflection;

public sealed class SettingsFormAiDetectionTests
{
    [StaFact]
    public void LoadAiDetectionFromSettings_ThenGetCurrentAiDetectionFormState_RoundTripsFields()
    {
        var originalSettings = Program._settings;
        Program._settings = LauncherSettings.CreateDefault();
        Program._settings.SuppressSave = true;
        try
        {
            using var form = new SettingsForm([], null);
            var settings = new AiDetectionSettings
            {
                Enabled = false,
                TimeoutSeconds = 600,
                ConfidenceThreshold = 0.75m,
                CopilotPath = @"C:\custom\copilot.exe",
                Model = "gpt-5.2"
            };

            form.LoadAiDetectionFromSettings(settings);
            var current = form.GetCurrentAiDetectionFormState();

            Assert.False(current.Enabled);
            Assert.Equal(600, current.TimeoutSeconds);
            Assert.Equal(0.75m, current.ConfidenceThreshold);
            Assert.Equal(@"C:\custom\copilot.exe", current.CopilotPath);
            Assert.Equal("gpt-5.2", current.Model);
        }
        finally
        {
            Program._settings = originalSettings;
        }
    }

    [StaFact]
    public void GetCurrentAiDetectionFormState_ProgrammaticControlChanges_ReturnsModifiedValues()
    {
        var originalSettings = Program._settings;
        Program._settings = LauncherSettings.CreateDefault();
        Program._settings.SuppressSave = true;
        try
        {
            using var form = new SettingsForm([], null);

            GetField<CheckBox>(form, "_aiEnabledCheck").Checked = false;
            GetField<NumericUpDown>(form, "_aiTimeoutSecondsBox").Value = 600;
            GetField<NumericUpDown>(form, "_aiConfidenceThresholdBox").Value = 0.75m;
            GetField<TextBox>(form, "_aiCopilotPathBox").Text = @"C:\custom\copilot.exe";
            GetField<TextBox>(form, "_aiModelBox").Text = "gpt-5.2";

            var current = form.GetCurrentAiDetectionFormState();

            Assert.False(current.Enabled);
            Assert.Equal(600, current.TimeoutSeconds);
            Assert.Equal(0.75m, current.ConfidenceThreshold);
            Assert.Equal(@"C:\custom\copilot.exe", current.CopilotPath);
            Assert.Equal("gpt-5.2", current.Model);
        }
        finally
        {
            Program._settings = originalSettings;
        }
    }

    private static T GetField<T>(SettingsForm form, string name)
        where T : class
    {
        var field = typeof(SettingsForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return Assert.IsType<T>(field!.GetValue(form));
    }
}
