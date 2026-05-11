using System.Reflection;

public sealed class SettingsFormAiDetectionTests
{
    private const string ModelDefaultDisplay = "(default — let Copilot decide)";
    private const string CustomSuffix = " (custom)";

    [StaFact]
    public void LoadAiDetectionFromSettings_ThenGetCurrentAiDetectionFormState_RoundTripsFields()
    {
        var originalSettings = Program._settings;
        Program._settings = LauncherSettings.CreateDefault();
        Program._settings.SuppressSave = true;
        try
        {
            using var form = new SettingsForm([], null);
            CancelModelFetch(form);
            var settings = new AiDetectionSettings
            {
                Enabled = false,
                TimeoutSeconds = 600,
                ConfidenceThreshold = 0.75m,
                Model = "gpt-5.2"
            };

            form.LoadAiDetectionFromSettings(settings);
            var current = form.GetCurrentAiDetectionFormState();

            Assert.False(current.Enabled);
            Assert.Equal(600, current.TimeoutSeconds);
            Assert.Equal(0.75m, current.ConfidenceThreshold);
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
            CancelModelFetch(form);

            GetField<CheckBox>(form, "_aiEnabledCheck").Checked = false;
            GetField<NumericUpDown>(form, "_aiTimeoutSecondsBox").Value = 600;
            GetField<NumericUpDown>(form, "_aiConfidenceThresholdBox").Value = 0.75m;
            var modelCombo = GetField<ComboBox>(form, "_aiModelCombo");
            modelCombo.Items.Add("gpt-5.2");
            modelCombo.SelectedItem = "gpt-5.2";

            var current = form.GetCurrentAiDetectionFormState();

            Assert.False(current.Enabled);
            Assert.Equal(600, current.TimeoutSeconds);
            Assert.Equal(0.75m, current.ConfidenceThreshold);
            Assert.Equal("gpt-5.2", current.Model);
        }
        finally
        {
            Program._settings = originalSettings;
        }
    }

    [StaFact]
    public void ModelCombo_UsesStrictDropDownListStyle()
    {
        var originalSettings = Program._settings;
        Program._settings = LauncherSettings.CreateDefault();
        Program._settings.SuppressSave = true;
        try
        {
            using var form = new SettingsForm([], null);
            CancelModelFetch(form);

            var modelCombo = GetField<ComboBox>(form, "_aiModelCombo");

            Assert.Equal(ComboBoxStyle.DropDownList, modelCombo.DropDownStyle);
        }
        finally
        {
            Program._settings = originalSettings;
        }
    }

    [StaFact]
    public void SaveAiDetection_DefaultModelSentinel_PersistsEmptyModel()
    {
        var originalSettings = Program._settings;
        Program._settings = LauncherSettings.CreateDefault();
        Program._settings.SuppressSave = true;
        try
        {
            using var form = new SettingsForm([], null);
            CancelModelFetch(form);

            form.LoadAiDetectionFromSettings(new AiDetectionSettings { Model = "" });
            var modelCombo = GetField<ComboBox>(form, "_aiModelCombo");

            Assert.Equal(ModelDefaultDisplay, modelCombo.SelectedItem);

            ClickSave(form);

            Assert.Equal("", Program._settings.AiDetection.Model);
        }
        finally
        {
            Program._settings = originalSettings;
        }
    }

    [StaFact]
    public void SaveAiDetection_KnownModelId_RoundTripsBareModelId()
    {
        var originalSettings = Program._settings;
        Program._settings = LauncherSettings.CreateDefault();
        Program._settings.SuppressSave = true;
        try
        {
            using var form = new SettingsForm([], null);
            CancelModelFetch(form);
            var modelCombo = GetField<ComboBox>(form, "_aiModelCombo");
            modelCombo.Items.Add("claude-sonnet-4.6");

            form.LoadAiDetectionFromSettings(new AiDetectionSettings { Model = "claude-sonnet-4.6" });

            Assert.Equal("claude-sonnet-4.6", modelCombo.SelectedItem);

            ClickSave(form);

            Assert.Equal("claude-sonnet-4.6", Program._settings.AiDetection.Model);
        }
        finally
        {
            Program._settings = originalSettings;
        }
    }

    [StaFact]
    public void LoadAndSaveAiDetection_UnknownSavedModel_AppendsCustomDisplayAndPersistsBareModelId()
    {
        var originalSettings = Program._settings;
        Program._settings = LauncherSettings.CreateDefault();
        Program._settings.SuppressSave = true;
        try
        {
            using var form = new SettingsForm([], null);
            CancelModelFetch(form);
            var modelCombo = GetField<ComboBox>(form, "_aiModelCombo");

            form.LoadAiDetectionFromSettings(new AiDetectionSettings { Model = "made-up-model-id-xyz" });

            var customItem = "made-up-model-id-xyz" + CustomSuffix;
            Assert.Contains(customItem, modelCombo.Items.Cast<object>().Select(item => item.ToString()));
            Assert.Equal(customItem, modelCombo.SelectedItem);

            ClickSave(form);

            Assert.Equal("made-up-model-id-xyz", Program._settings.AiDetection.Model);
        }
        finally
        {
            Program._settings = originalSettings;
        }
    }

    [StaFact]
    public void SaveAiDetection_SwitchingFromCustomToDefaultSentinel_PersistsEmptyModel()
    {
        var originalSettings = Program._settings;
        Program._settings = LauncherSettings.CreateDefault();
        Program._settings.SuppressSave = true;
        try
        {
            using var form = new SettingsForm([], null);
            CancelModelFetch(form);
            var modelCombo = GetField<ComboBox>(form, "_aiModelCombo");
            form.LoadAiDetectionFromSettings(new AiDetectionSettings { Model = "made-up-model-id-xyz" });

            modelCombo.SelectedItem = ModelDefaultDisplay;
            ClickSave(form);

            Assert.Equal("", Program._settings.AiDetection.Model);
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

    private static void CancelModelFetch(SettingsForm form)
    {
        var field = typeof(SettingsForm).GetField("_modelFetchCts", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        var cts = Assert.IsType<CancellationTokenSource>(field!.GetValue(form));
        cts.Cancel();
    }

    private static void ClickSave(SettingsForm form)
    {
        var saveButton = FindButton(form.Controls, "Save");
        Assert.NotNull(saveButton);
        var onClick = typeof(Control).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(onClick);
        onClick!.Invoke(saveButton, [EventArgs.Empty]);
    }

    private static Button? FindButton(Control.ControlCollection controls, string text)
    {
        foreach (Control control in controls)
        {
            if (control is Button button && string.Equals(button.Text, text, StringComparison.Ordinal))
            {
                return button;
            }

            var child = FindButton(control.Controls, text);
            if (child != null)
            {
                return child;
            }
        }

        return null;
    }
}
