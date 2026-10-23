using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Windows.Forms;

// Usage: WinFormsBloodChecks.exe <built NGUInjector.dll> <NGUIdle_Data\Managed>
// Runs compiled Blood/global-toggle builders and handlers with the real model/disk store.
// Bootstrap only the base Form: full Unity/Mono form startup and unrelated stage layouts
// are not supported by this desktop .NET Framework test host.
internal static class Program
{
    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private static readonly string[] Fields = { "AutoBloodSpellsEnabled", "BloodSpaghettiGoalPercent",
        "BloodCounterfeitGoalPercent", "IronPillSaveSeconds", "IronPillMinWorth" };
    private static Assembly Plugin;
    private static Type MainType, PlanType;
    private static object Settings, Store;
    private static Form Ui;
    private static readonly List<Control> Groups = new List<Control>();
    private static string Temp, Plans;
    private static int Assertions;

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            var managed = Path.GetFullPath(args[1]);
            AppDomain.CurrentDomain.AssemblyResolve += (sender, e) =>
            {
                var path = Path.Combine(managed, new AssemblyName(e.Name).Name + ".dll");
                return File.Exists(path) ? Assembly.LoadFrom(path) : null;
            };
            Plugin = Assembly.LoadFrom(Path.GetFullPath(args[0]));
            MainType = Plugin.GetType("NGUInjector.Main", true);
            PlanType = Plugin.GetType("NGUInjector.CustomPlans.CustomPlan", true);
            Temp = Path.Combine(Path.GetTempPath(), "opencode", "WinFormsBlood-" + Guid.NewGuid().ToString("N"));
            Plans = Path.Combine(Temp, "custom-plans");
            Directory.CreateDirectory(Plans);
            Settings = Activator.CreateInstance(Plugin.GetType("NGUInjector.SavedSettings", true), new object[] { null });
            Set(MainType, "Settings", Settings);
            // No settings save path: global setters cannot call Unity's native JSON serializer.
            // Refresh() invokes the real control loaders under the form's initialization guard.
            Property(Settings, "CustomAutoBloodSpellsEnabled", true);
            Property(Settings, "CustomBloodSpaghettiGoalPercent", 123);
            Property(Settings, "CustomBloodCounterfeitGoalPercent", 456);
            Property(Settings, "CustomIronPillSaveSeconds", 900);
            Property(Settings, "CustomIronPillMinWorth", 1.25d);
            Property(Settings, "UseCustomRebirthPlan", false);
            Property(Settings, "CustomAdventureMaxItopodDeaths", 3);
            Store = Activator.CreateInstance(Plugin.GetType("NGUInjector.CustomPlans.CustomPlanStore", true), All, null, new object[] { Plans }, null);
            Set(MainType, "CustomPlanStore", Store);
            Ui = (Form)FormatterServices.GetUninitializedObject(Plugin.GetType("NGUInjector.SettingsForm", true));
            typeof(Form).GetConstructor(Type.EmptyTypes).Invoke(Ui, new object[0]);
            Set(Ui, "_initializing", true);
            Set(Ui, "_customStageRows", Activator.CreateInstance(Ui.GetType().GetField("_customStageRows", All).FieldType));
            Set(Ui, "moneyPitError", new ErrorProvider());
            Set(Ui, "_customStatus", new Label());
            foreach (var builder in new[] { "BuildCustomBloodSpellsGroup", "BuildCustomAutoBoostGroup", "BuildCustomMoneyPitGroup" })
                Groups.Add((Control)Call(Ui, builder));

            Console.WriteLine("Checking inherited loads");
            CheckLoadWithoutWrites();
            Console.WriteLine("Checking individual edits");
            CheckIndividualEdits();
            Console.WriteLine("Checking unrelated edits");
            CheckUnrelatedEdits();
            Console.WriteLine("Checking worth validation");
            CheckWorthValidation();
            Console.WriteLine("Checking plan switching and focus");
            CheckSwitchingAndFocus();
            Console.WriteLine("Checking telemetry scope");
            CheckTelemetryScope();
            Console.WriteLine("PASS: " + Assertions + " assertions against compiled WinForms Blood Magic controls and plan persistence.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally
        {
            Ui?.Dispose();
            foreach (var group in Groups) group.Dispose();
            if (Ui != null) ((ErrorProvider)Get(Ui, "moneyPitError")).Dispose();
            if (Temp != null && Directory.Exists(Temp)) Directory.Delete(Temp, true);
        }
    }

    private static void CheckLoadWithoutWrites()
    {
        foreach (var explicitNull in new[] { false, true })
        {
            var name = explicitNull ? "nulls" : "missing";
            Save(name, NewPlan());
            var path = Path.Combine(Plans, name + ".json");
            if (explicitNull)
            {
                var text = File.ReadAllText(path).TrimEnd();
                text = text.Substring(0, text.Length - 1) + "," + string.Join(",", Fields.Select(x => "\"" + x + "\":null")) + "}";
                File.WriteAllText(path, text);
            }
            var before = File.ReadAllText(path);
            Select(name);
            Equal(true, Control<CheckBox>("_customAutoBloodSpells").Checked, "inherits global spell permission");
            Equal(true, Control<CheckBox>("_customAutoIronPill").Checked, "legacy pill permission remains enabled");
            Equal(123m, Control<NumericUpDown>("_customBloodSpaghettiGoalPercent").Value, "inherits spaghetti");
            Equal(456m, Control<NumericUpDown>("_customBloodCounterfeitGoalPercent").Value, "inherits counterfeit");
            Equal(900m, Control<NumericUpDown>("_customIronPillSaveSeconds").Value, "inherits saving time");
            Equal("1.25", Control<TextBox>("_customIronPillMinWorth").Text, "inherits worth");
            Equal(false, ValidateWorth().Cancel, "untouched inherited worth validates");
            Equal(before, File.ReadAllText(path), "loading and validating missing/null fields never writes the plan");
            AssertUnset(Load(name), "legacy fields remain unset");
        }
    }

    private static void CheckIndividualEdits()
    {
        Save("untouched", NewPlan());
        var untouched = File.ReadAllText(Path.Combine(Plans, "untouched.json"));
        var controls = new[] { "_customAutoBloodSpells", "_customBloodSpaghettiGoalPercent", "_customBloodCounterfeitGoalPercent",
            "_customIronPillSaveSeconds", "_customIronPillMinWorth", "_customAutoIronPill" };
        for (var i = 0; i < controls.Length; i++)
        {
            Save("individual", NewPlan());
            Select("individual");
            var editor = Get(Ui, controls[i]);
            if (editor is CheckBox toggle) toggle.Checked = !toggle.Checked;
            else if (editor is NumericUpDown number) number.Value = 0;
            else { ((TextBox)editor).Text = "0"; Equal(false, ValidateWorth().Cancel, "explicit zero worth validates"); }
            var plan = Load("individual");
            for (var j = 0; j < Fields.Length; j++)
                Equal(i == j ? (j == 0 ? (object)false : j == 4 ? 0d : (object)0) : null,
                    Get(plan, Fields[j]), "only edited field becomes an override: " + controls[i] + " / " + Fields[j]);
            Equal(i != 5, Get(plan, "AutoIronPillEnabled"), "pill permission is independent");
            Equal(untouched, File.ReadAllText(Path.Combine(Plans, "untouched.json")), "another plan is byte-identical");
            CheckGlobals();
        }
    }

    private static void CheckUnrelatedEdits()
    {
        Save("unrelated", NewPlan());
        Select("unrelated");
        var before = File.ReadAllText(Path.Combine(Plans, "unrelated.json"));
        Control<CheckBox>("_customAutoMoneyPit").Checked = true;
        Equal(true, Property(Settings, "CustomAutoMoneyPitEnabled"), "unrelated global toggle really saved");
        Refresh();
        AssertUnset(Load("unrelated"), "unrelated global edit retains inheritance");
        Equal(before, File.ReadAllText(Path.Combine(Plans, "unrelated.json")), "unrelated global edit never writes the plan");
        CheckGlobals();
        Property(Settings, "CustomBloodSpaghettiGoalPercent", 789);
        Refresh();
        Equal(789m, Control<NumericUpDown>("_customBloodSpaghettiGoalPercent").Value, "unset field follows new global value");
        AssertUnset(Load("unrelated"), "global blood change does not snapshot inherited values");
        Property(Settings, "CustomBloodSpaghettiGoalPercent", 123);
    }

    private static void CheckWorthValidation()
    {
        Save("worth", NewPlan());
        Select("worth");
        var path = Path.Combine(Plans, "worth.json");
        var before = File.ReadAllText(path);
        var editor = Control<TextBox>("_customIronPillMinWorth");
        foreach (var invalid in new[] { "", "bad", "-1", "NaN", "Infinity", "1e9" })
        {
            editor.Text = invalid;
            Equal(true, ValidateWorth().Cancel, "invalid worth cancels validation: " + invalid);
            Equal(before, File.ReadAllText(path), "invalid worth leaves plan byte-identical");
        }
        editor.Text = "1.25";
        Equal(false, ValidateWorth().Cancel, "reverting to the loaded value validates");
        Equal(string.Empty, ((ErrorProvider)Get(Ui, "moneyPitError")).GetError(editor), "reverting clears the validation error");
        editor.Text = "1.25e0";
        Equal(false, ValidateWorth().Cancel, "equivalent scientific notation validates");
        Equal(before, File.ReadAllText(path), "equivalent worth does not materialize inheritance");
        foreach (var value in new[] { 0.125d, 1e8 })
        {
            editor.Text = value.ToString("E", System.Globalization.CultureInfo.InvariantCulture);
            Equal(false, ValidateWorth().Cancel, "finite scientific worth validates");
            Equal(value, Get(Load("worth"), "IronPillMinWorth"), "scientific worth persists to selected plan");
            foreach (var field in Fields.Take(4)) Equal(null, Get(Load("worth"), field), "worth edit leaves other overrides unset");
            CheckGlobals();
        }
    }

    private static void CheckSwitchingAndFocus()
    {
        var plan = NewPlan();
        Set(plan, "AutoBloodSpellsEnabled", false);
        Set(plan, "AutoIronPillEnabled", false);
        Set(plan, "BloodSpaghettiGoalPercent", 5);
        Set(plan, "BloodCounterfeitGoalPercent", 6);
        Set(plan, "IronPillSaveSeconds", 7);
        Set(plan, "IronPillMinWorth", 8d);
        Save("override", plan);
        Select("missing");
        var worth = Control<TextBox>("_customIronPillMinWorth");
        Ui.Controls.Add(worth);
        Ui.Show();
        worth.Focus();
        Application.DoEvents();
        Equal(true, worth.Focused, "worth editor is focused during refresh regression");
        Property(Settings, "CustomIronPillMinWorth", 99d);
        Refresh();
        Equal("1.25", worth.Text, "same-plan refresh preserves focused text");
        Equal(false, ValidateWorth().Cancel, "untouched old inherited text validates after global change");
        Equal(null, Get(Load("missing"), "IronPillMinWorth"), "global change while focused does not create an override");
        // External selection while worth remains focused must still refresh every control.
        Select("override");
        Equal(false, Control<CheckBox>("_customAutoBloodSpells").Checked, "switch refreshes spell toggle");
        Equal(false, Control<CheckBox>("_customAutoIronPill").Checked, "switch refreshes pill permission");
        Equal(5m, Control<NumericUpDown>("_customBloodSpaghettiGoalPercent").Value, "switch refreshes spaghetti");
        Equal(6m, Control<NumericUpDown>("_customBloodCounterfeitGoalPercent").Value, "switch refreshes counterfeit");
        Equal(7m, Control<NumericUpDown>("_customIronPillSaveSeconds").Value, "switch refreshes saving time");
        Equal("8", worth.Text, "switch replaces focused worth from previous plan");
        worth.Text = "not a number";
        Equal(true, ValidateWorth().Cancel, "invalid text sets validation error before switching");
        Select("missing");
        Equal("99", worth.Text, "selected-plan reload refreshes inherited worth even with focus/error");
        Equal(string.Empty, ((ErrorProvider)Get(Ui, "moneyPitError")).GetError(worth), "switch clears prior plan's validation error");
        var savingTime = Control<NumericUpDown>("_customIronPillSaveSeconds");
        Ui.Controls.Add(savingTime);
        savingTime.Focus();
        Equal(true, savingTime.Focused, "saving-time editor is focused during switching regression");
        Select("override");
        Equal(7m, savingTime.Value, "switch replaces focused saving time from previous plan");
        Ui.Hide();
        Property(Settings, "CustomIronPillMinWorth", 1.25d);
    }

    private static void CheckTelemetryScope()
    {
        Property(Settings, "UseCustomRebirthPlan", false);
        Select("override");
        Call(Ui, "UpdateCustomBloodGoalEstimates");
        var text = Control<Label>("_customBloodSpaghettiEstimate").Text;
        Equal(true, text.Contains("Runtime globals (plan stopped / Quick)"), "stopped/Quick telemetry explicitly uses runtime globals");
        Equal(true, text.Contains("not the selected plan's overrides"), "runtime telemetry is not a stopped plan preview");
        Equal(5m, Control<NumericUpDown>("_customBloodSpaghettiGoalPercent").Value, "stopped plan controls still edit selected overrides");
        Equal(true, Control<CheckBox>("_customAutoBloodSpells").Parent.Parent.Text.Contains("Selected Custom Plan"), "group title identifies editing scope");
    }

    private static object NewPlan() => Activator.CreateInstance(PlanType, true);
    private static object Load(string name) => Call(Store, "Load", name);
    private static void Save(string name, object plan) => Call(Store, "Save", name, plan);
    private static void Select(string name) { Property(Settings, "CustomPlanFile", name); Refresh(); }
    private static void Refresh()
    {
        Set(Ui, "_customPlanInitializing", true);
        try
        {
            Call(Ui, "LoadCustomTogglesIntoUi", Settings);
            Call(Ui, "LoadCustomBloodSettingsIntoUi", Load((string)Property(Settings, "CustomPlanFile")));
        }
        finally { Set(Ui, "_customPlanInitializing", false); Set(Ui, "_initializing", false); }
    }
    private static T Control<T>(string name) where T : Control => (T)Get(Ui, name);
    private static CancelEventArgs ValidateWorth()
    {
        var args = new CancelEventArgs();
        Call(Control<TextBox>("_customIronPillMinWorth"), "OnValidating", args);
        return args;
    }
    private static void AssertUnset(object plan, string message)
    {
        foreach (var field in Fields) Equal(null, Get(plan, field), message + ": " + field);
    }
    private static void CheckGlobals()
    {
        Equal(true, Property(Settings, "CustomAutoBloodSpellsEnabled"), "global spell permission unchanged");
        Equal(123, Property(Settings, "CustomBloodSpaghettiGoalPercent"), "global spaghetti unchanged");
        Equal(456, Property(Settings, "CustomBloodCounterfeitGoalPercent"), "global counterfeit unchanged");
        Equal(900, Property(Settings, "CustomIronPillSaveSeconds"), "global saving time unchanged");
        Equal(1.25d, Property(Settings, "CustomIronPillMinWorth"), "global worth unchanged");
    }
    private static object Get(object target, string field) => (target as Type ?? target.GetType()).GetField(field, All).GetValue(target is Type ? null : target);
    private static void Set(object target, string field, object value) => (target as Type ?? target.GetType()).GetField(field, All).SetValue(target is Type ? null : target, value);
    private static object Property(object target, string name) => target.GetType().GetProperty(name, All).GetValue(target);
    private static void Property(object target, string name, object value) => target.GetType().GetProperty(name, All).SetValue(target, value);
    private static object Call(object target, string name, params object[] args)
    {
        try { return target.GetType().GetMethod(name, All).Invoke(target, args); }
        catch (TargetInvocationException error) { throw error.InnerException ?? error; }
    }
    private static void Equal(object expected, object actual, string message)
    {
        Assertions++;
        if (!Equals(expected, actual)) throw new Exception(message + ": expected " + (expected ?? "<null>") + ", got " + (actual ?? "<null>"));
    }
}
