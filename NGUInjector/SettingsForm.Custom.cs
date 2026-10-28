using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using NGUInjector.CustomPlans;
using NGUInjector.Managers;
using SimpleJSON;

namespace NGUInjector
{
    public partial class SettingsForm
    {
        private const int CustomLabelColumnWidth = 170;
        private const int CustomEditorMinWidth = 260;
        private const int CustomWideEditorMinWidth = 620;
        private int _customFrontTabCount;
        private bool _customPlanInitializing;
        private ComboBox _customPlanSelector;
        private CheckBox _customUsePlan;
        private CheckBox _customGlobalAutoRebirth;
        private CheckBox _customPlanAutoRebirth;
        private CheckBox _customTimeEnabled;
        private NumericUpDown _customHours;
        private NumericUpDown _customMinutes;
        private NumericUpDown _customSeconds;
        private CheckBox _customNumberEnabled;
        private TextBox _customNumberTarget;
        private ComboBox _customTriggerMode;
        private CheckBox _customAutoActivateFruits;
        private CheckBox _customAutoMoneyPit;
        private CheckBox _customAutoBoost;
        private ComboBox _customAutoBoostCubeRatio;
        private CheckBox _customAutoBloodSpells;
        private CheckBox _customAutoIronPill;
        private string _customBloodPlanFile;
        private NumericUpDown _customBloodSpaghettiGoalPercent;
        private NumericUpDown _customBloodCounterfeitGoalPercent;
        private Label _customBloodSpaghettiEstimate;
        private Label _customBloodCounterfeitEstimate;
        private NumericUpDown _customIronPillSaveSeconds;
        private TextBox _customIronPillMinWorth;
        private string _customIronPillMinWorthLoadedText;
        private TextBox _customIronPillHistory;
        private CheckBox _customStageSchedulerEnabled;
        private NumericUpDown _customStageHours;
        private NumericUpDown _customStageMinutes;
        private NumericUpDown _customStageSeconds;
        private TextBox _customStageSummary;
        private CustomPlan _customStageSummaryPlan;
        private string _customStageRowsRevision;
        private JSONObject _customStageActionProgress;
        private bool _customStageOverridePending;
        private Button _customStageSkipButton;
        private Button _customStageRestartButton;
        private ToolTip _customStageToolTip;
        private FlowLayoutPanel _customStageSteps;
        private readonly List<CustomStageEditorRow> _customStageRows = new List<CustomStageEditorRow>();
        private bool _customStageRowsInitializing;
        private CheckBox _customAdventureEnabled;
        private ComboBox _customAdventureMode;
        private NumericUpDown _customAdventureMaxItopodDeaths;
        private ComboBox _customAdventureZone;
        private CheckBox _customAdventureGoldPush;
        private CheckBox _manualAdventureEnabled;
        private ComboBox _manualAdventureZone;
        private CheckBox _manualAdventureBossOnly;
        private CheckBox _manualAdventureRecoverHealth;
        private CheckBox _manualAdventurePrecastBuffs;
        private CheckBox _manualAdventureFastCombat;
        private CheckBox _manualAdventureBeastMode;
        private CheckBox _manualAdventureAllowFallback;
        private Label _manualAdventureStatus;
        private ComboBox _customGoalPresetSelector = null;
        private TextBox _customGoalPresetName = null;
        private ComboBox _customGearGoal;
        private ComboBox _customFightScoreMode;
        private Label _customPlanStatus;
        private Label _customRuntimeStatus;
        private Label _customStatus;
        private NumericUpDown _customAugmentsBloodEstimateSeconds;
        private TextBox _customAugmentPreview;
        private TextBox _customRebirthHistory;
        private DateTime _lastCustomRuntimeStatusUpdate = DateTime.MinValue;
        private readonly Dictionary<CustomFocusGoal, Button> _customFocusButtons = new Dictionary<CustomFocusGoal, Button>();

        private sealed class CustomStageEditorRow
        {
            internal GroupBox Container;
            internal TextBox Name;
            internal ComboBox Preset;
            internal ComboBox CompletionMode;
            internal TextBox Percent;
            internal NumericUpDown FixedSeconds;
            internal Label FixedSecondsLabel;
            internal TextBox TargetPower;
            internal TextBox TargetToughness;
            internal TextBox EnergyPriorities;
            internal TextBox MagicPriorities;
            internal NumericUpDown BlackBarLookaheadSeconds;
            internal CheckBox FightScheduleEnabled;
            internal ComboBox FightAnchor;
            internal NumericUpDown FightDelaySeconds;
            internal NumericUpDown FightIntervalSeconds;
            internal Label DragHandle;
            internal Label Details;
            internal Label Duration;
            internal Label PercentLabel;
            internal Label TargetPowerLabel;
            internal Label TargetToughnessLabel;
            internal Button DeleteButton;
            internal Button SetCurrentButton;
            internal CustomPlanStage SourceStage;
        }

        private sealed class CustomStagePresetDefinition
        {
            internal int Id;
            internal string Name;
            internal int EnergyFocus;
            internal int MagicFocus;
            internal int GearGoal;
            internal string Details;
        }

        private void RemoveLegacyTabs()
        {
            tabControl1.TabPages.Remove(tabPage1);  // Legacy general settings
            tabControl1.TabPages.Remove(tabPage2);  // Legacy allocation/profile automation
            tabControl1.TabPages.Remove(tabPage3);  // Yggdrasil
            tabControl1.TabPages.Remove(tabPage4);  // Inventory
            tabControl1.TabPages.Remove(tabPage5);  // Titans
            tabControl1.TabPages.Remove(tabPage6);  // Legacy combat/quests
            tabControl1.TabPages.Remove(tabPage7);  // Gold
            tabControl1.TabPages.Remove(tabPage8);  // Wishes
            tabControl1.TabPages.Remove(tabPage9);  // Hacks/R3
            tabControl1.TabPages.Remove(tabPage10); // The Pit
        }

        private void AddCustomTabToFront(TabPage tab)
        {
            tabControl1.TabPages.Insert(_customFrontTabCount, tab);
            _customFrontTabCount++;
        }

        private void BuildCustomTab()
        {
            var tab = new TabPage("Rebirth") {AutoScroll = false, Padding = new Padding(0)};
            var scrollHost = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                Padding = new Padding(12)
            };
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(0),
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            root.Controls.Add(BuildRuntimeStatusGroup(), 0, 0);
            root.Controls.Add(BuildFocusGoalsGroup(), 0, 1);
            root.Controls.Add(BuildPlanGroup(), 0, 2);

            scrollHost.Controls.Add(root);
            tab.Controls.Add(scrollHost);
            AddCustomTabToFront(tab);
        }

        private void BuildCustomMiscTab()
        {
            var tab = new TabPage("Tools") {AutoScroll = false, Padding = new Padding(0)};
            var scrollHost = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                Padding = new Padding(12)
            };
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                RowCount = 2,
                Padding = new Padding(0)
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.Controls.Add(BuildGoalLoadoutGroup(), 0, 0);
            root.Controls.Add(BuildAugmentOptimizerGroup(), 0, 1);

            scrollHost.Controls.Add(root);
            tab.Controls.Add(scrollHost);
            AddCustomTabToFront(tab);
        }

        private void BuildAdventureTab()
        {
            var tab = new TabPage("Adventure") {AutoScroll = false, Padding = new Padding(0)};
            var scrollHost = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                Padding = new Padding(12)
            };
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                RowCount = 2,
                Padding = new Padding(0)
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.Controls.Add(BuildAdventureGoalGroup(), 0, 0);
            root.Controls.Add(BuildManualAdventureGroup(), 0, 1);

            scrollHost.Controls.Add(root);
            tab.Controls.Add(scrollHost);
            AddCustomTabToFront(tab);
        }

        private void BuildCustomDebugTab()
        {
            var tab = new TabPage("Debug") {AutoScroll = false, Padding = new Padding(0)};
            var scrollHost = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                Padding = new Padding(12)
            };
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(0)
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.Controls.Add(BuildRebirthHistoryGroup(), 0, 0);
            root.Controls.Add(BuildDiagnosticsGroup(), 0, 1);
            root.Controls.Add(BuildFutureGroup(), 0, 2);

            scrollHost.Controls.Add(root);
            tab.Controls.Add(scrollHost);
            AddCustomTabToFront(tab);
        }

        private void BuildCustomTogglesTab()
        {
            var wasInitializing = _customPlanInitializing;
            _customPlanInitializing = true;
            try
            {
                var tab = new TabPage("Automation") {AutoScroll = false, Padding = new Padding(0)};
                var scrollHost = new Panel
                {
                    Dock = DockStyle.Fill,
                    AutoScroll = true,
                    Padding = new Padding(12)
                };
                var root = new TableLayoutPanel
                {
                    Dock = DockStyle.Top,
                    AutoSize = true,
                    AutoSizeMode = AutoSizeMode.GrowAndShrink,
                    ColumnCount = 1,
                    RowCount = 4,
                    Padding = new Padding(0)
                };
                root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                root.Controls.Add(BuildCustomAutoBoostGroup(), 0, 0);
                root.Controls.Add(BuildCustomBloodSpellsGroup(), 0, 1);
                root.Controls.Add(BuildCustomMoneyPitGroup(), 0, 2);
                root.Controls.Add(BuildCustomFruitTogglesGroup(), 0, 3);

                scrollHost.Controls.Add(root);
                tab.Controls.Add(scrollHost);
                AddCustomTabToFront(tab);
            }
            finally
            {
                _customPlanInitializing = wasInitializing;
            }
        }

        private Control BuildCustomAutoBoostGroup()
        {
            var group = NewGroup("Auto Boost");
            var layout = NewTwoColumnLayout();

            _customAutoBoost = NewCheckBox("Auto boost");
            _customAutoBoost.CheckedChanged += CustomTogglesControl_Changed;
            layout.Controls.Add(NewLabel("Enabled"), 0, 0);
            layout.Controls.Add(_customAutoBoost, 1, 0);

            _customAutoBoostCubeRatio = NewComboBox(220);
            _customAutoBoostCubeRatio.DataSource = new BindingSource(new Dictionary<int, string>
            {
                {1, "1:1 Power:Toughness"},
                {2, "2:1 Power:Toughness"},
                {3, "3:1 Power:Toughness"},
                {4, "4:1 Power:Toughness"}
            }.ToList(), null);
            _customAutoBoostCubeRatio.ValueMember = "Key";
            _customAutoBoostCubeRatio.DisplayMember = "Value";
            _customAutoBoostCubeRatio.SelectedIndexChanged += CustomTogglesControl_Changed;
            layout.Controls.Add(NewLabel("Cube ratio"), 0, 1);
            layout.Controls.Add(_customAutoBoostCubeRatio, 1, 1);

            var note = new Label
            {
                AutoSize = true,
                MaximumSize = new Size(700, 0),
                Margin = new Padding(3, 5, 10, 5),
                Text = "Auto boost runs whenever enabled, even if global automation is off. It finishes locked boost items below level 100 when possible, then boosts weapons/equipped gear, locked auto-merge gear, hearts last, and sends leftovers to the Infinity Cube using the selected Power:Toughness ratio."
            };
            layout.Controls.Add(new Label(), 0, 2);
            layout.Controls.Add(note, 1, 2);

            group.Controls.Add(layout);
            return group;
        }

        private Control BuildCustomBloodSpellsGroup()
        {
            var group = NewGroup("Blood Magic (Selected Custom Plan)");
            var layout = NewTwoColumnLayout();

            _customAutoBloodSpells = NewCheckBox("Auto-manage Blood Magic spell toggles");
            _customAutoBloodSpells.CheckedChanged += CustomBloodControl_Changed;
            layout.Controls.Add(NewLabel("Enabled"), 0, 0);
            layout.Controls.Add(_customAutoBloodSpells, 1, 0);

            var spaghettiPanel = NewInlineFlowPanel();
            _customBloodSpaghettiGoalPercent = NewSettingsNumber(0, 100000);
            _customBloodSpaghettiGoalPercent.ValueChanged += CustomBloodControl_Changed;
            spaghettiPanel.Controls.Add(_customBloodSpaghettiGoalPercent);
            spaghettiPanel.Controls.Add(NewInlineLabel("% loot bonus before turning Spaghetti off"));
            layout.Controls.Add(NewLabel("Spaghetti goal"), 0, 1);
            layout.Controls.Add(spaghettiPanel, 1, 1);

            var counterfeitPanel = NewInlineFlowPanel();
            _customBloodCounterfeitGoalPercent = NewSettingsNumber(0, 100000);
            _customBloodCounterfeitGoalPercent.ValueChanged += CustomBloodControl_Changed;
            counterfeitPanel.Controls.Add(_customBloodCounterfeitGoalPercent);
            counterfeitPanel.Controls.Add(NewInlineLabel("% gold bonus before turning Counterfeit Gold off"));
            layout.Controls.Add(NewLabel("Counterfeit goal"), 0, 2);
            layout.Controls.Add(counterfeitPanel, 1, 2);

            _customBloodSpaghettiEstimate = NewLabel("Waiting for blood data...");
            _customBloodCounterfeitEstimate = NewLabel("Waiting for blood data...");
            _customBloodSpaghettiEstimate.MaximumSize = new Size(700, 0);
            _customBloodCounterfeitEstimate.MaximumSize = new Size(700, 0);
            layout.Controls.Add(NewLabel("Spaghetti estimate"), 0, 3);
            layout.Controls.Add(_customBloodSpaghettiEstimate, 1, 3);
            layout.Controls.Add(NewLabel("Counterfeit estimate"), 0, 4);
            layout.Controls.Add(_customBloodCounterfeitEstimate, 1, 4);

            _customAutoIronPill = NewCheckBox("Allow automatic Iron Pill for the selected plan");
            _customAutoIronPill.CheckedChanged += CustomBloodControl_Changed;
            layout.Controls.Add(NewLabel("Iron Pill permission"), 0, 5);
            layout.Controls.Add(_customAutoIronPill, 1, 5);

            var ironWaitPanel = NewInlineFlowPanel();
            _customIronPillSaveSeconds = NewSettingsNumber(0, 604800);
            _customIronPillSaveSeconds.Value = 900;
            _customIronPillSaveSeconds.ValueChanged += CustomBloodControl_Changed;
            ironWaitPanel.Controls.Add(_customIronPillSaveSeconds);
            ironWaitPanel.Controls.Add(NewInlineLabel("seconds saving blood (0 disables wait threshold)"));
            layout.Controls.Add(NewLabel("Iron Pill save time"), 0, 6);
            layout.Controls.Add(ironWaitPanel, 1, 6);

            var ironWorthPanel = NewInlineFlowPanel();
            _customIronPillMinWorth = NewTextBox(180);
            _customIronPillMinWorth.Text = "0";
            _customIronPillMinWorth.Validating += (sender, args) =>
            {
                if (_initializing || _customPlanInitializing)
                    return;
                if (_customIronPillMinWorth.Text == _customIronPillMinWorthLoadedText)
                {
                    moneyPitError.SetError(_customIronPillMinWorth, string.Empty);
                    return;
                }
                if (!double.TryParse(_customIronPillMinWorth.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var worth)
                    || double.IsNaN(worth) || double.IsInfinity(worth) || worth < 0d || worth > 1e8)
                {
                    args.Cancel = true;
                    moneyPitError.SetError(_customIronPillMinWorth, "Enter a finite number from 0 to 1e8 (100 million); 0 disables the worth threshold.");
                    return;
                }
                moneyPitError.SetError(_customIronPillMinWorth, string.Empty);
                CustomBloodControl_Changed(sender, EventArgs.Empty);
            };
            _customIronPillMinWorth.KeyDown += (sender, args) =>
            {
                if (args.KeyCode != Keys.Enter)
                    return;
                args.SuppressKeyPress = true;
                Validate();
            };
            ironWorthPanel.Controls.Add(_customIronPillMinWorth);
            ironWorthPanel.Controls.Add(NewInlineLabel("0..1e8 (100 million); scientific notation allowed. Saves on leaving or Enter."));
            layout.Controls.Add(NewLabel("Iron Pill min worth"), 0, 7);
            layout.Controls.Add(ironWorthPanel, 1, 7);

            var note = new Label
            {
                AutoSize = true,
                MaximumSize = new Size(700, 0),
                Margin = new Padding(3, 5, 10, 5),
                Text = "Controls edit the selected Custom plan, even while stopped. Unset values inherit legacy global settings until individually edited; Quick without a running plan uses globals, not these overrides. Estimates show runtime scope below, not a stopped plan preview. Cast history covers all plans. Estimates assume all magic funds the optimal sustainable ritual with current gear, gold income, and spell split; they are read-only projections. Automatic Iron Pill requires plan permission, auto blood spells, active Custom plan resource automation, rebirth time >= 1h, and boss stage >= 100. Begin saving when ready or within the cooldown lookahead (900s in worth-only mode). Spend existing blood on NUMBER once, then pause other automatic spells. Blood Magic moves first in the magic order, except Time Machine and Wandoos stay first during their stages. Energy, gear, targets, and stage timing are unchanged. Cast when ready and EITHER saving time OR worth is reached; zero disables a threshold, both zero cast as soon as ready. Defaults: 900s saving time, 0 worth. Prep expires after max(900, saving seconds) + 60s; timeout or a failed NUMBER cast restores normal operation for 900s. Pill worth is base Power and Toughness gained each, capped at 100 million. Manual/boss preparation and ready automatic rebirth preempt saving. Outside reservation, Energy NGU stages and Quick Energy NGU prioritize blood until enabled Spaghetti/Counterfeit goals are reached, then restore the saved order. This does not flush blood; dedicated Magic/All NGU keep their order."
            };
            layout.Controls.Add(new Label(), 0, 8);
            layout.Controls.Add(note, 1, 8);

            _customIronPillHistory = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                Dock = DockStyle.Fill,
                MinimumSize = new Size(CustomEditorMinWidth, 145),
                Height = 145,
                Font = new Font(FontFamily.GenericMonospace, 8.25f),
                Text = "No successful automatic Iron Pill casts recorded yet."
            };
            layout.Controls.Add(NewLabel("Last 10 casts (all plans)"), 0, 9);
            layout.Controls.Add(_customIronPillHistory, 1, 9);

            group.Controls.Add(layout);
            return group;
        }

        private Control BuildCustomMoneyPitGroup()
        {
            var group = NewGroup("Money Pit");
            var layout = NewTwoColumnLayout();

            _customAutoMoneyPit = NewCheckBox("Auto Money Pit");
            _customAutoMoneyPit.CheckedChanged += CustomTogglesControl_Changed;
            layout.Controls.Add(NewLabel("Enabled"), 0, 0);
            layout.Controls.Add(_customAutoMoneyPit, 1, 0);

            var note = new Label
            {
                AutoSize = true,
                MaximumSize = new Size(700, 0),
                Margin = new Padding(3, 5, 10, 5),
                Text = "Custom Money Pit runs whenever enabled, even if legacy/global automation is off. It throws when the pit is ready and gold is above the configured reserve, then logs the reward to pitspin.log."
            };
            layout.Controls.Add(new Label(), 0, 1);
            layout.Controls.Add(note, 1, 1);

            group.Controls.Add(layout);
            return group;
        }

        private Control BuildCustomFruitTogglesGroup()
        {
            var group = NewGroup("Fruit / Rebirth Toggles");
            var layout = NewTwoColumnLayout();

            var toggles = NewStackPanel();
            _customAutoActivateFruits = NewPlanCheckBox("Activate affordable fruits during active Custom plan/adventure automation over 1h");
            toggles.Controls.Add(_customAutoActivateFruits);
            layout.Controls.Add(NewLabel("Plan toggles"), 0, 0);
            layout.Controls.Add(toggles, 1, 0);

            var note = new Label
            {
                AutoSize = true,
                MaximumSize = new Size(700, 0),
                Margin = new Padding(3, 5, 10, 5),
                Text = "These stay with the selected Custom plan. Fruit of Power alpha is consumed only during rebirth prep, never by Fight bosses."
            };
            layout.Controls.Add(new Label(), 0, 1);
            layout.Controls.Add(note, 1, 1);

            group.Controls.Add(layout);
            return group;
        }

        private Control BuildRuntimeStatusGroup()
        {
            var group = NewGroup("Status");
            var layout = NewSingleColumnLayout();

            _customRuntimeStatus = new Label
            {
                AutoSize = true,
                MaximumSize = new Size(900, 0),
                ForeColor = Color.DarkSlateGray,
                Font = new Font(FontFamily.GenericSansSerif, 12.5f, FontStyle.Bold),
                Padding = new Padding(8),
                Margin = new Padding(0, 2, 0, 4),
                Text = Main.GetCustomAutomationStatus()
            };
            layout.Controls.Add(_customRuntimeStatus);
            group.Controls.Add(layout);
            return group;
        }

        private Control BuildFocusGoalsGroup()
        {
            var group = NewGroup("Focus Goals");
            var layout = NewSingleColumnLayout();

            var manualButtons = NewInlineFlowPanel();
            manualButtons.Controls.Add(NewButton("Fight bosses", CustomRunFightPrep_Click));
            manualButtons.Controls.Add(NewButton("Rebirth now", CustomRunRebirth_Click));
            layout.Controls.Add(manualButtons, 0, 0);

            var estimatePanel = NewInlineFlowPanel();
            _customAugmentsBloodEstimateSeconds = NewSettingsNumber(1, 100000);
            _customAugmentsBloodEstimateSeconds.ValueChanged += CustomFocusSettingsControl_Changed;
            estimatePanel.Controls.Add(NewInlineLabel("Augments + Blood estimate"));
            estimatePanel.Controls.Add(_customAugmentsBloodEstimateSeconds);
            estimatePanel.Controls.Add(NewInlineLabel("s"));
            layout.Controls.Add(estimatePanel, 0, 1);

            var buttons = NewWrappingFlowPanel();
            buttons.Controls.Add(NewFocusButton("Time Machine", CustomFocusGoal.TimeMachine));
            buttons.Controls.Add(NewFocusButton("Augments + Blood", CustomFocusGoal.AugmentsBloodMagic));
            buttons.Controls.Add(NewFocusButton("Wandoos", CustomFocusGoal.Wandoos));
            buttons.Controls.Add(NewFocusButton("Energy NGU", CustomFocusGoal.EnergyNgu));
            buttons.Controls.Add(NewFocusButton("Magic NGU", CustomFocusGoal.MagicNgu));
            buttons.Controls.Add(NewFocusButton("All NGU", CustomFocusGoal.Ngu));
            buttons.Controls.Add(NewFocusButton("Advanced Training", CustomFocusGoal.AdvancedTraining));
            buttons.Controls.Add(NewFocusButton("Adventure", CustomFocusGoal.Adventure));
            buttons.Controls.Add(NewFocusButton("Manual Titans", CustomFocusGoal.ManualTitans));
            layout.Controls.Add(buttons, 0, 2);

            var note = new Label
            {
                AutoSize = true,
                MaximumSize = new Size(760, 0),
                Margin = new Padding(3, 5, 10, 5),
                Text = "Focus buttons pause auto-rebirth/custom rebirth, recall resources, settle briefly around gear swaps, then apply generated equipment, resource allocation, beards, and weighted diggers for that goal. The Augments + Blood estimate controls augment target scoring. Active sustained focus is highlighted blue; click it again to stop and release focused resources. Fight bosses never rebirths; Rebirth now is separate."
            };
            layout.Controls.Add(note, 0, 3);

            group.Controls.Add(layout);
            return group;
        }

        private Control BuildPlanGroup()
        {
            var group = NewGroup("Custom Rebirth");
            var layout = NewSingleColumnLayout();

            var currentGroup = NewInnerGroup("Current Preset");
            var currentLayout = NewTwoColumnLayout();

            var planPanel = NewWrappingFlowPanel();
            planPanel.MaximumSize = new Size(CustomWideEditorMinWidth, 0);
            _customPlanSelector = NewComboBox(260);
            _customPlanSelector.SelectedIndexChanged += CustomPlanSelector_SelectedIndexChanged;
            planPanel.Controls.Add(_customPlanSelector);
            planPanel.Controls.Add(NewSmallButton("24h NGU", (sender, args) => CustomUseBuiltInPlan_Click("24hr", true)));
            planPanel.Controls.Add(NewSmallButton("15m NUMBER", (sender, args) => CustomUseBuiltInPlan_Click("15m-NUMBER-increase", true)));
            planPanel.Controls.Add(NewSmallButton("30m NUMBER", (sender, args) => CustomUseBuiltInPlan_Click("30m-NUMBER-increase", true)));
            planPanel.Controls.Add(NewSmallButton("1h1m NUMBER", (sender, args) => CustomUseBuiltInPlan_Click("1h-1m-NUMBER-increase", true)));
            planPanel.Controls.Add(NewSmallButton("New", CustomPlanNew_Click));
            planPanel.Controls.Add(NewSmallButton("Copy", CustomPlanCopy_Click));
            planPanel.Controls.Add(NewSmallButton("Delete", CustomPlanDelete_Click));
            currentLayout.Controls.Add(NewLabel("Plan"), 0, 0);
            currentLayout.Controls.Add(planPanel, 1, 0);

            _customPlanStatus = new Label
            {
                AutoSize = true,
                MaximumSize = new Size(760, 0),
                ForeColor = Color.DarkSlateGray,
                Margin = new Padding(3, 4, 10, 6),
                Text = "Plan status will appear here."
            };
            currentLayout.Controls.Add(NewLabel("State"), 0, 1);
            currentLayout.Controls.Add(_customPlanStatus, 1, 1);

            var enablePanel = NewInlineFlowPanel();
            _customGlobalAutoRebirth = NewCheckBox("Custom auto-rebirth");
            _customGlobalAutoRebirth.CheckedChanged += CustomGlobalAutoRebirth_CheckedChanged;
            enablePanel.Controls.Add(_customGlobalAutoRebirth);
            _customUsePlan = NewCheckBox("Run selected Custom plan");
            _customUsePlan.CheckedChanged += CustomUsePlan_CheckedChanged;
            enablePanel.Controls.Add(_customUsePlan);
            _customPlanAutoRebirth = NewPlanCheckBox("Selected plan can trigger rebirth");
            enablePanel.Controls.Add(_customPlanAutoRebirth);
            currentLayout.Controls.Add(NewLabel("Enable"), 0, 2);
            currentLayout.Controls.Add(enablePanel, 1, 2);
            currentGroup.Controls.Add(currentLayout);
            layout.Controls.Add(currentGroup);

            var triggerGroup = NewInnerGroup("When to Rebirth");
            var triggerLayout = NewTwoColumnLayout();
            var timePanel = NewInlineFlowPanel();
            _customTimeEnabled = NewPlanCheckBox("Enabled");
            _customHours = NewNumber(0, 100000);
            _customMinutes = NewNumber(0, 59);
            _customSeconds = NewNumber(0, 59);
            timePanel.Controls.Add(_customTimeEnabled);
            timePanel.Controls.Add(NewInlineLabel("after"));
            timePanel.Controls.Add(_customHours);
            timePanel.Controls.Add(NewInlineLabel("h"));
            timePanel.Controls.Add(_customMinutes);
            timePanel.Controls.Add(NewInlineLabel("m"));
            timePanel.Controls.Add(_customSeconds);
            timePanel.Controls.Add(NewInlineLabel("s"));
            triggerLayout.Controls.Add(NewLabel("Time"), 0, 0);
            triggerLayout.Controls.Add(timePanel, 1, 0);

            var numberPanel = NewInlineFlowPanel();
            _customNumberEnabled = NewPlanCheckBox("Enabled");
            _customNumberTarget = NewTextBox(90);
            _customNumberTarget.TextChanged += CustomPlanControl_Changed;
            numberPanel.Controls.Add(_customNumberEnabled);
            numberPanel.Controls.Add(NewInlineLabel("current x >="));
            numberPanel.Controls.Add(_customNumberTarget);
            triggerLayout.Controls.Add(NewLabel("NUMBER"), 0, 1);
            triggerLayout.Controls.Add(numberPanel, 1, 1);

            _customTriggerMode = NewComboBox(190);
            _customTriggerMode.Items.AddRange(new object[] {"Any enabled trigger", "All enabled triggers"});
            _customTriggerMode.SelectedIndexChanged += CustomPlanControl_Changed;
            triggerLayout.Controls.Add(NewLabel("Mode"), 0, 2);
            triggerLayout.Controls.Add(_customTriggerMode, 1, 2);
            triggerGroup.Controls.Add(triggerLayout);
            layout.Controls.Add(triggerGroup);

            var stepsGroup = NewInnerGroup("Steps");
            var stepsLayout = NewSingleColumnLayout();
            var durationPanel = NewInlineFlowPanel();
            _customStageSchedulerEnabled = NewPlanCheckBox("Use stages");
            durationPanel.Controls.Add(_customStageSchedulerEnabled);
            _customStageHours = NewNumber(0, 100000);
            _customStageMinutes = NewNumber(0, 59);
            _customStageSeconds = NewNumber(0, 59);
            durationPanel.Controls.Add(NewInlineLabel("planned length"));
            durationPanel.Controls.Add(_customStageHours);
            durationPanel.Controls.Add(NewInlineLabel("h"));
            durationPanel.Controls.Add(_customStageMinutes);
            durationPanel.Controls.Add(NewInlineLabel("m"));
            durationPanel.Controls.Add(_customStageSeconds);
            durationPanel.Controls.Add(NewInlineLabel("s"));
            stepsLayout.Controls.Add(durationPanel);

            stepsLayout.Controls.Add(new Label
            {
                AutoSize = true,
                MaximumSize = new Size(CustomWideEditorMinWidth, 0),
                ForeColor = Color.DimGray,
                Margin = new Padding(3, 4, 10, 6),
                Text = "Fixed duration: positive seconds require actual applied time. Flexible share: 0 seconds shares the remaining budget by weight; delays can shrink this to zero. AT targets: mandatory, with no fixed completion time. Automatic rebirth waits for fixed work and AT targets. The budget uses the enabled positive rebirth time, otherwise planned length, even for all-fixed plans. Row previews are nominal estimates; the summary shows reported runtime progress. Preset and AT-condition changes preserve fixed seconds; set seconds to 0 explicitly to use a flexible share."
            });

            var stageEditor = NewStackPanel();
            stageEditor.MaximumSize = new Size(CustomWideEditorMinWidth, 0);
            var stageButtons = NewInlineFlowPanel();
            stageButtons.Controls.Add(NewButton("AT 6k/6k -> NGU", CustomUseAdvancedTrainingNguStages_Click));
            stageButtons.Controls.Add(NewButton("Use 8/15/30m NUMBER stages", CustomUseNumberClimbStages_Click));
            stageEditor.Controls.Add(stageButtons);

            _customStageSteps = NewStackPanel();
            _customStageSteps.MinimumSize = new Size(CustomWideEditorMinWidth, 0);
            _customStageSteps.AllowDrop = true;
            _customStageSteps.DragEnter += CustomStageRows_DragEnter;
            _customStageSteps.DragDrop += CustomStageRows_DragDrop;
            stageEditor.Controls.Add(_customStageSteps);
            stageEditor.Controls.Add(NewButton("Add new step at bottom", CustomStageAdd_Click));
            stepsLayout.Controls.Add(stageEditor);

            _customStageSummary = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                Dock = DockStyle.Fill,
                MinimumSize = new Size(CustomWideEditorMinWidth, 140),
                Height = 140,
                Margin = new Padding(3, 4, 10, 6),
                Font = new Font(FontFamily.GenericMonospace, 8.25f),
                BackColor = SystemColors.Control
            };
            stepsLayout.Controls.Add(_customStageSummary);
            var stageActions = NewInlineFlowPanel();
            _customStageSkipButton = NewButton("Skip current", (sender, args) => CustomOverrideStage("skip"));
            _customStageRestartButton = NewButton("Restart stage", (sender, args) => CustomOverrideStage("restart"));
            _customStageSkipButton.Enabled = false;
            _customStageRestartButton.Enabled = false;
            _customStageSkipButton.AccessibleDescription = "Immediately skips required work; automatic rebirth may happen earlier.";
            _customStageRestartButton.AccessibleDescription = "Immediately restarts the currently observed stage from its beginning.";
            stageActions.Controls.Add(_customStageSkipButton);
            stageActions.Controls.Add(_customStageRestartButton);
            stepsLayout.Controls.Add(stageActions);
            stepsGroup.Controls.Add(stepsLayout);
            layout.Controls.Add(stepsGroup);

            _customStatus = new Label {AutoSize = true, MaximumSize = new Size(700, 0), ForeColor = SystemColors.ControlDarkDark, Margin = new Padding(3, 5, 10, 5)};
            layout.Controls.Add(_customStatus);

            group.Controls.Add(layout);
            return group;
        }

        private Control BuildAdventureGoalGroup()
        {
            var group = NewGroup("Adventure Goal");
            var layout = NewTwoColumnLayout();

            _customAdventureEnabled = NewCheckBox("Manage adventure goal from Custom automation");
            _customAdventureEnabled.CheckedChanged += CustomAdventureControl_Changed;
            layout.Controls.Add(NewLabel("Enabled"), 0, 0);
            layout.Controls.Add(_customAdventureEnabled, 1, 0);

            _customAdventureMode = NewComboBox(220);
            _customAdventureMode.Items.AddRange(new object[] {"Optimal ITOPOD", "Farm zone", "Farm boosts", "Max ITOPOD push"});
            _customAdventureMode.SelectedIndexChanged += CustomAdventureControl_Changed;
            layout.Controls.Add(NewLabel("Adventure goal"), 0, 1);
            layout.Controls.Add(_customAdventureMode, 1, 1);

            _customAdventureZone = NewComboBox(280);
            _customAdventureZone.SelectedIndexChanged += CustomAdventureControl_Changed;
            layout.Controls.Add(NewLabel("Farm zone"), 0, 2);
            layout.Controls.Add(_customAdventureZone, 1, 2);

            _customAdventureGoldPush = NewCheckBox("Try the highest unlocked zone boss in 90% Gold / 10% combat gear during the first hour; on death, try the next lower zone");
            _customAdventureGoldPush.CheckedChanged += CustomAdventureControl_Changed;
            layout.Controls.Add(NewLabel("Gold push"), 0, 3);
            layout.Controls.Add(_customAdventureGoldPush, 1, 3);

            _customAdventureMaxItopodDeaths = NewSettingsNumber(1, 1000);
            _customAdventureMaxItopodDeaths.ValueChanged += CustomAdventureControl_Changed;
            layout.Controls.Add(NewLabel("Max ITOPOD death limit"), 0, 4);
            layout.Controls.Add(_customAdventureMaxItopodDeaths, 1, 4);

            var note = new Label
            {
                AutoSize = true,
                MaximumSize = new Size(700, 0),
                Margin = new Padding(3, 5, 10, 5),
                Text = "Max ITOPOD push climbs until the death limit, then switches Adventure mode to Optimal ITOPOD. Changing the limit applies to the next push. Farm boosts chooses the best unlocked boost-farming target from current adventure stats, drop chance, boost recycling, and active boost filters, then updates as stats change. If a fixed farm zone is locked, Custom falls back to optimized ITOPOD and says so in the Now status. Gold push is independent: during the first 60 minutes it starts at each newly highest unlocked zone boss in generated 90% Gold / 10% power-and-toughness gear. Each death heals and retries one lower unlocked non-titan zone; a kill or exhausting the zones restores gear and returns to the selected adventure goal. Configured titan gold targets try gold gear three seconds before spawn, then required power/toughness/regen gear if needed. All titan swaps must pass native autokill checks and restore after completion."
            };
            layout.Controls.Add(new Label(), 0, 5);
            layout.Controls.Add(note, 1, 5);

            group.Controls.Add(layout);
            return group;
        }

        private Control BuildManualAdventureGroup()
        {
            var group = NewGroup("Manual Adventure Fighting");
            var layout = NewTwoColumnLayout();

            _manualAdventureEnabled = NewCheckBox("Enable manual adventure fighting");
            _manualAdventureEnabled.CheckedChanged += ManualAdventureControl_Changed;
            layout.Controls.Add(NewLabel("Enabled"), 0, 0);
            layout.Controls.Add(_manualAdventureEnabled, 1, 0);

            _manualAdventureZone = NewComboBox(280);
            _manualAdventureZone.SelectedIndexChanged += ManualAdventureControl_Changed;
            layout.Controls.Add(NewLabel("Target zone"), 0, 1);
            layout.Controls.Add(_manualAdventureZone, 1, 1);

            var options = NewStackPanel();
            _manualAdventureBossOnly = NewCheckBox("Bosses only");
            _manualAdventureRecoverHealth = NewCheckBox("Recover in Safe Zone before fighting");
            _manualAdventurePrecastBuffs = NewCheckBox("Precast Charge/Parry/buffs before fighting");
            _manualAdventureFastCombat = NewCheckBox("Fast combat moves");
            _manualAdventureBeastMode = NewCheckBox("Use Beast Mode");
            _manualAdventureAllowFallback = NewCheckBox("If target is locked, fight the highest unlocked normal zone");
            _manualAdventureBossOnly.CheckedChanged += ManualAdventureControl_Changed;
            _manualAdventureRecoverHealth.CheckedChanged += ManualAdventureControl_Changed;
            _manualAdventurePrecastBuffs.CheckedChanged += ManualAdventureControl_Changed;
            _manualAdventureFastCombat.CheckedChanged += ManualAdventureControl_Changed;
            _manualAdventureBeastMode.CheckedChanged += ManualAdventureControl_Changed;
            _manualAdventureAllowFallback.CheckedChanged += ManualAdventureControl_Changed;
            options.Controls.Add(_manualAdventureBossOnly);
            options.Controls.Add(_manualAdventureRecoverHealth);
            options.Controls.Add(_manualAdventurePrecastBuffs);
            options.Controls.Add(_manualAdventureFastCombat);
            options.Controls.Add(_manualAdventureBeastMode);
            options.Controls.Add(_manualAdventureAllowFallback);
            layout.Controls.Add(NewLabel("Options"), 0, 2);
            layout.Controls.Add(options, 1, 2);

            _manualAdventureStatus = new Label
            {
                AutoSize = true,
                MaximumSize = new Size(700, 0),
                ForeColor = Color.DarkSlateGray,
                Margin = new Padding(3, 5, 10, 5),
                Text = Main.GetCustomAutomationStatus()
            };
            layout.Controls.Add(NewLabel("Now"), 0, 3);
            layout.Controls.Add(_manualAdventureStatus, 1, 3);

            var note = new Label
            {
                AutoSize = true,
                MaximumSize = new Size(700, 0),
                Margin = new Padding(3, 5, 10, 5),
                Text = "This uses the current safe adventure router and manual combat routine, without enabling legacy automation. While enabled, it takes over normal adventure routing from the Custom adventure goal. Custom rebirth prep, boss push, and rebirth-ready states pause it."
            };
            layout.Controls.Add(new Label(), 0, 4);
            layout.Controls.Add(note, 1, 4);

            group.Controls.Add(layout);
            return group;
        }

        private Control BuildGoalLoadoutGroup()
        {
            var group = NewGroup("Equipment Loadout");
            var layout = NewTwoColumnLayout();

            _customGearGoal = NewComboBox(240);
            _customGearGoal.DataSource = new BindingSource(GearOptimizer.GoalLabels.ToList(), null);
            _customGearGoal.ValueMember = "Key";
            _customGearGoal.DisplayMember = "Value";
            _customGearGoal.SelectedIndexChanged += CustomPlanControl_Changed;
            layout.Controls.Add(NewLabel("Generated goal"), 0, 0);
            layout.Controls.Add(_customGearGoal, 1, 0);

            _customFightScoreMode = NewComboBox(280);
            _customFightScoreMode.DataSource = new BindingSource(GearOptimizer.FightScoreModeLabels.ToList(), null);
            _customFightScoreMode.ValueMember = "Key";
            _customFightScoreMode.DisplayMember = "Value";
            _customFightScoreMode.SelectedIndexChanged += CustomPlanControl_Changed;
            layout.Controls.Add(NewLabel("Fight scoring"), 0, 1);
            layout.Controls.Add(_customFightScoreMode, 1, 1);

            var buttons = NewInlineFlowPanel();
            buttons.Controls.Add(NewButton("Apply generated loadout now", CustomOptimizeGear_Click));
            buttons.Controls.Add(NewButton("Pre-cache planned loadouts", CustomPreCacheLoadouts_Click));
            layout.Controls.Add(NewLabel("Actions"), 0, 2);
            layout.Controls.Add(buttons, 1, 2);

            var note = new Label
            {
                AutoSize = true,
                MaximumSize = new Size(700, 0),
                Margin = new Padding(3, 5, 10, 5),
                Text = "Equipment loadouts are generated from owned gear by goal. Pre-cache planned loadouts prepares the active rebirth/adventure gear goals so stage swaps do not need to re-optimize every time."
            };
            layout.Controls.Add(new Label(), 0, 3);
            layout.Controls.Add(note, 1, 3);

            group.Controls.Add(layout);
            return group;
        }

        private Control BuildAugmentOptimizerGroup()
        {
            var group = NewGroup("Augment Optimizer");
            var layout = NewTwoColumnLayout();

            var buttons = NewInlineFlowPanel();
            buttons.Controls.Add(NewButton("Preview best augment", CustomPreviewAugment_Click));
            buttons.Controls.Add(NewButton("Allocate best augment now", CustomAllocateAugment_Click));
            layout.Controls.Add(NewLabel("Actions"), 0, 0);
            layout.Controls.Add(buttons, 1, 0);

            _customAugmentPreview = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Dock = DockStyle.Fill,
                MinimumSize = new Size(CustomWideEditorMinWidth, 72),
                Height = 72,
                Margin = new Padding(3, 4, 10, 6)
            };
            layout.Controls.Add(NewLabel("Preview"), 0, 1);
            layout.Controls.Add(_customAugmentPreview, 1, 1);

            var note = new Label
            {
                AutoSize = true,
                MaximumSize = new Size(700, 0),
                Margin = new Padding(3, 5, 10, 5),
                Text = "The optimizer uses the Augments + Blood estimate from Focus Goals, then estimates expected augment work from energy allocation, energy power, augment speed multipliers, and projected gold budget."
            };
            layout.Controls.Add(new Label(), 0, 2);
            layout.Controls.Add(note, 1, 2);

            group.Controls.Add(layout);
            return group;
        }

        private Control BuildRebirthHistoryGroup()
        {
            var group = NewGroup("Rebirth History");
            _customRebirthHistory = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Dock = DockStyle.Top,
                MinimumSize = new Size(CustomWideEditorMinWidth, 125),
                Height = 120,
                Margin = new Padding(3, 8, 10, 6),
                Font = new Font(FontFamily.GenericMonospace, 8.25f)
            };
            group.Controls.Add(_customRebirthHistory);
            return group;
        }

        private Control BuildDiagnosticsGroup()
        {
            var group = NewGroup("Diagnostics / Probe");
            var layout = NewTwoColumnLayout();

            var buttons = NewInlineFlowPanel();
            buttons.Controls.Add(NewButton("Run safe probe", CustomRunSafeProbe_Click));
            buttons.Controls.Add(NewButton("Run probe.txt commands", CustomRunProbeCommands_Click));
            layout.Controls.Add(NewLabel("Probe"), 0, 0);
            layout.Controls.Add(buttons, 1, 0);

            var note = new Label
            {
                AutoSize = true,
                MaximumSize = new Size(700, 0),
                Margin = new Padding(3, 5, 10, 5),
                Text = "Safe probe only reads state and writes a report under logs. probe.txt commands are explicit live actions from Desktop\\NGUInjector\\probe.txt."
            };
            layout.Controls.Add(new Label(), 0, 1);
            layout.Controls.Add(note, 1, 1);

            group.Controls.Add(layout);
            return group;
        }

        private Control BuildFutureGroup()
        {
            var group = NewGroup("Planned Next Pieces");
            var label = new Label
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                MaximumSize = new Size(700, 0),
                Margin = new Padding(3, 8, 10, 8),
                Text = "Notes: custom auto-rebirth now supports NUMBER-climb triggers, percent stages, adventure ITOPOD/boost/gold-push routing, augment optimization, and rebirth history. Open questions are tracked in CUSTOM_AUTOMATION_QUESTIONS.md."
            };
            group.Controls.Add(label);
            return group;
        }

        internal void UpdateCustomTabFromSettings(SavedSettings settings)
        {
            if (_customPlanSelector == null || Main.CustomPlanStore == null)
                return;

            _customPlanInitializing = true;
            try
            {
                _customUsePlan.Checked = settings.UseCustomRebirthPlan;
                _customGlobalAutoRebirth.Checked = settings.AutoRebirth;
                LoadCustomFocusSettingsIntoUi(settings);
                LoadCustomTogglesIntoUi(settings);
                LoadCustomAdventureIntoUi(settings);
                LoadManualAdventureIntoUi(settings);
                RefreshCustomPlanSelector(settings.CustomPlanFile);
                LoadCustomPlanIntoUi(Main.CustomPlanStore.Load(settings.CustomPlanFile));
            }
            catch (Exception e)
            {
                Main.LogError("Update Custom tab", e);
            }
            finally
            {
                _customPlanInitializing = false;
            }

            UpdateCustomFocusButtonHighlights();
            UpdateCustomBloodGoalEstimates();
            RefreshCustomIronPillHistory(settings);
        }

        private void UpdateCustomBloodGoalEstimates()
        {
            var estimates = Main.GetBloodGoalEstimates();
            var scope = Main.Settings?.UseCustomRebirthPlan == true
                ? $"Runtime selected plan: '{Main.Settings.CustomPlanFile}'"
                : "Runtime globals (plan stopped / Quick); not the selected plan's overrides";
            var labels = new[] { _customBloodSpaghettiEstimate, _customBloodCounterfeitEstimate };
            for (var i = 0; i < labels.Length; i++)
            {
                if (labels[i] == null)
                    continue;
                if (estimates.Length != 2)
                {
                    labels[i].Text = scope + "\nBlood data unavailable";
                    continue;
                }
                var estimate = estimates[i];
                var eta = estimate.Status;
                if (estimate.Status == "Casting" && estimate.EstimatedSeconds.HasValue)
                {
                    var total = Math.Ceiling(estimate.EstimatedSeconds.Value);
                    eta = $"about {Math.Floor(total / 3600):0}h {Math.Floor(total % 3600 / 60):0}m {total % 60:0}s";
                }
                var remaining = estimate.RemainingBlood.HasValue ? $" | Blood needed: {FormatCompact(estimate.RemainingBlood.Value)}" : string.Empty;
                labels[i].Text = $"{scope}\nCurrent: {estimate.CurrentPercent:0}% / Goal: {estimate.GoalPercent}% | Time to goal with all magic: {eta}{remaining}\n{estimate.ProductionAssumption} | Projected spell share: {FormatCompact(estimate.BloodPerSecond)} blood/s";
            }
        }

        private void RefreshCustomPlanSelector(string selectedPlan)
        {
            var plans = Main.CustomPlanStore.ListPlanNames();
            _customPlanSelector.DataSource = null;
            _customPlanSelector.DataSource = plans;
            _customPlanSelector.SelectedItem = plans.Contains(selectedPlan) ? selectedPlan : plans.FirstOrDefault();
        }

        private void LoadCustomPlanIntoUi(CustomPlan plan)
        {
            LoadCustomBloodSettingsIntoUi(plan);
            _customPlanAutoRebirth.Checked = plan.AutoRebirthEnabled;
            _customTimeEnabled.Checked = plan.TimeTriggerEnabled;
            _customHours.Value = Math.Min(_customHours.Maximum, plan.TimeTriggerSeconds / 3600);
            _customMinutes.Value = Math.Min(_customMinutes.Maximum, plan.TimeTriggerSeconds % 3600 / 60);
            _customSeconds.Value = Math.Min(_customSeconds.Maximum, plan.TimeTriggerSeconds % 60);
            _customNumberEnabled.Checked = plan.NumberTriggerEnabled;
            _customNumberTarget.Text = plan.NumberMultiplierTarget.ToString(CultureInfo.InvariantCulture);
            _customTriggerMode.SelectedIndex = plan.TriggerMode == 1 ? 1 : 0;
            _customAutoActivateFruits.Checked = plan.AutoActivateFruits;
            _customStageSchedulerEnabled.Checked = plan.StageSchedulerEnabled;
            _customStageHours.Value = Math.Min(_customStageHours.Maximum, plan.StageDurationSeconds / 3600);
            _customStageMinutes.Value = Math.Min(_customStageMinutes.Maximum, plan.StageDurationSeconds % 3600 / 60);
            _customStageSeconds.Value = Math.Min(_customStageSeconds.Maximum, plan.StageDurationSeconds % 60);
            LoadCustomStageRows(plan.Stages);
            _customStageRowsRevision = plan.RuntimeRevision;
            _customFightScoreMode.SelectedValue = GearOptimizer.FightScoreModeLabels.ContainsKey(plan.FightScoreMode) ? plan.FightScoreMode : 0;
            LoadGoalPresetsIntoUi(plan);
            RefreshCustomHistory(plan);
            UpdateCustomRebirthDashboard(plan);
            UpdateCustomBloodGoalEstimates();
            SetCustomStatus($"Loaded '{Main.Settings.CustomPlanFile}'");
        }

        private void SaveCustomPlanFromUi()
        {
            if (_customPlanInitializing || Main.CustomPlanStore == null)
                return;

            if (!double.TryParse(_customNumberTarget.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var numberTarget) || numberTarget < 0)
            {
                SetCustomStatus("Number trigger target must be a non-negative number.");
                return;
            }

            if (!TryReadStageRows(out var stages, out var stageError))
            {
                SetCustomStatus(stageError, true);
                return;
            }

            var planName = Main.Settings.CustomPlanFile;
            var plan = Main.CustomPlanStore.Load(planName);
            plan.AutoRebirthEnabled = _customPlanAutoRebirth.Checked;
            plan.TimeTriggerEnabled = _customTimeEnabled.Checked;
            plan.TimeTriggerSeconds = decimal.ToInt32(_customHours.Value) * 3600
                                      + decimal.ToInt32(_customMinutes.Value) * 60
                                      + decimal.ToInt32(_customSeconds.Value);
            plan.NumberTriggerEnabled = _customNumberEnabled.Checked;
            plan.NumberMultiplierTarget = numberTarget;
            plan.TriggerMode = Math.Max(0, _customTriggerMode.SelectedIndex);
            plan.AutoActivateFruits = _customAutoActivateFruits.Checked;
            plan.StageSchedulerEnabled = _customStageSchedulerEnabled.Checked;
            plan.StageDurationSeconds = decimal.ToInt32(_customStageHours.Value) * 3600
                                        + decimal.ToInt32(_customStageMinutes.Value) * 60
                                        + decimal.ToInt32(_customStageSeconds.Value);
            plan.Stages = stages;
            plan.FightScoreMode = _customFightScoreMode.SelectedValue is int fightScoreMode ? fightScoreMode : 0;
            SaveGoalPresetFromUi(plan);
            Main.CustomPlanStore.Save(planName, plan);
            _customStageRowsRevision = plan.RuntimeRevision;
            UpdateCustomRebirthDashboard(plan);
            RefreshStageRowPreviews();
            SetCustomStatus($"Auto-saved '{planName}'");
        }

        private void UpdateCustomRebirthDashboard(CustomPlan plan)
        {
            if (plan == null)
                return;

            _customStageSummaryPlan = plan;
            UpdateCustomStageRuntimeDetails(Main.reference?.GetCustomPlanProgress());
            if (_customPlanStatus != null)
            {
                var mode = plan.TriggerMode == 1 ? "all triggers" : "any trigger";
                _customPlanStatus.Text = $"Custom rebirth: {OnOff(Main.Settings.AutoRebirth)}   Plan running: {OnOff(Main.Settings.UseCustomRebirthPlan)}   Plan trigger: {OnOff(plan.AutoRebirthEnabled)}   Mode: {mode}";
            }

            if (_customStageSummary != null)
            {
                var summary = BuildStageSummary(plan, _customStageActionProgress);
                if (_customStageSummary.Text != summary)
                    _customStageSummary.Text = summary;
            }
        }

        private static string OnOff(bool value)
        {
            return value ? "on" : "off";
        }

        private static string BuildStageSummary(CustomPlan plan, JSONObject progress)
        {
            if (plan == null)
                return "No plan loaded.";

            if (!plan.StageSchedulerEnabled)
                return "Stages are disabled. Rebirth triggers still apply.";

            if (plan.Stages == null || plan.Stages.Length == 0)
                return "No stages are configured.";

            var snapshot = CustomStageAllocator.GetSchedulerSnapshot(plan, Main.Character?.rebirthTime?.totalseconds ?? 0d);
            if (snapshot == null)
                return "Stage timing is unavailable.";

            var initialized = snapshot.ActiveStageIndex >= 0;
            var lines = new List<string>();
            lines.Add(snapshot.ProtectedWorkComplete
                ? "Automatic rebirth stage gate: clear (other triggers still apply)."
                : "Automatic rebirth stage gate: blocked by fixed work, AT targets, or unconfirmed progress.");
            lines.Add(initialized
                ? $"Fixed applied time remaining: {snapshot.ProtectedSecondsRemaining:0.#}s."
                : "Fixed applied time remaining: not yet reported; nominal estimates only.");
            lines.Add(snapshot.ScheduleComplete ? "Schedule complete; holding last allocation."
                : initialized ? $"Current step: {snapshot.ActiveStageIndex + 1}; cycle {snapshot.Cycle:0}." : "Waiting for timing to initialize.");
            if (!string.IsNullOrEmpty(snapshot.TimingReason))
                lines.Add(snapshot.TimingReason);

            for (var i = 0; i < plan.Stages.Length; i++)
            {
                var stage = plan.Stages[i];
                var active = i == snapshot.ActiveStageIndex && !snapshot.ScheduleComplete;
                var reported = progress == null ? null : progress["stages"][i];
                var state = reported != null && reported["state"].IsString ? reported["state"].Value
                    : active ? "current" : "not reported";
                string timing;
                if (stage.CompletionMode == (int) CustomStageCompletionMode.AdvancedTrainingStats)
                {
                    timing = $"AT targets {FormatCompact(stage.TargetAdvancedTrainingPower)}/{FormatCompact(stage.TargetAdvancedTrainingToughness)}; indeterminate duration";
                }
                else if (stage.DurationSeconds > 0)
                {
                    timing = $"Fixed duration {CustomStageAllocator.FormatSeconds(stage.DurationSeconds)}";
                    if (active)
                        timing += $"; credited {snapshot.StageElapsedSeconds:0.#}s; remaining {Math.Max(0d, stage.DurationSeconds - snapshot.StageElapsedSeconds):0.#}s";
                }
                else
                {
                    var assigned = snapshot.DurationSeconds[i];
                    timing = $"Flexible share ({stage.Percent:0.###} weight); {(initialized ? "assigned" : "nominal estimate")} {CustomStageAllocator.FormatSeconds(assigned)}";
                    if (active)
                        timing += $"; elapsed {snapshot.StageElapsedSeconds:0.#}s; remaining {Math.Max(0d, assigned - snapshot.StageElapsedSeconds):0.#}s";
                }
                var gear = stage.GearGoal >= 0 && GearOptimizer.GoalLabels.ContainsKey(stage.GearGoal)
                    ? GearOptimizer.GoalLabels[stage.GearGoal]
                    : "Current";
                lines.Add($"{i + 1}. {stage.Name} [{state}]: {timing}");
                if (reported != null)
                    lines.Add("  " + CustomStageRuntimeDescription(reported));
                lines.Add($"  Energy: {CustomStageAllocator.FocusLabel(stage.EnergyFocus)} | Magic: {CustomStageAllocator.FocusLabel(stage.MagicFocus)} | Gear: {gear}");
                if (stage.EnergyPriorities != null && stage.EnergyPriorities.Length > 0)
                    lines.Add("  Energy override: " + string.Join(" > ", stage.EnergyPriorities));
                if (stage.MagicPriorities != null && stage.MagicPriorities.Length > 0)
                    lines.Add("  Magic override: " + string.Join(" > ", stage.MagicPriorities));
            }

            return string.Join(Environment.NewLine, lines.ToArray());
        }

        private bool CustomStageActionsReady(JSONObject progress)
        {
            var selected = _customPlanSelector?.SelectedItem as string;
            return !_initializing && !_customPlanInitializing && !_customStageRowsInitializing
                   && Main.reference != null && Main.CustomPlanStore != null && Main.Settings != null
                   && Main.Settings.UseCustomRebirthPlan && selected != null && selected == Main.Settings.CustomPlanFile
                   && progress != null && progress["available"].AsBool && progress["status"].Value == "running"
                   && progress["planName"].Value == selected && !string.IsNullOrEmpty(_customStageRowsRevision)
                   && progress["planRevision"].Value == _customStageRowsRevision
                   && progress["stageEntry"].IsString && !string.IsNullOrEmpty(progress["stageEntry"].Value)
                   && progress["activeStageIndex"].IsNumber && progress["activeStageIndex"].AsInt >= 0
                   && progress["activeStageIndex"].AsDouble == progress["activeStageIndex"].AsInt
                   && progress["activeStageIndex"].AsInt < _customStageRows.Count
                   && progress["stages"].IsArray && progress["stages"].Count == _customStageRows.Count;
        }

        private void CustomOverrideStage(string action, int stageIndex = -1)
        {
            var observed = _customStageActionProgress;
            if (_customStageOverridePending || !CustomStageActionsReady(observed))
            {
                SetCustomStatus("Stage actions require the selected running plan and reported current progress.", true);
                return;
            }

            _customStageOverridePending = true;
            try
            {
                UpdateCustomStageRuntimeDetails(observed);
                var name = _customPlanSelector.SelectedItem.ToString();
                var entry = observed["stageEntry"].Value;
                var plan = Main.CustomPlanStore.Load(name);
                var current = Main.reference.GetCustomPlanProgress();
                // Never substitute a newly observed entry for the one the user acted on.
                if (string.IsNullOrEmpty(plan.RuntimeRevision) || plan.RuntimeRevision != observed["planRevision"].Value
                    || !CustomStageActionsReady(current) || current["stageEntry"].Value != entry)
                    throw new InvalidOperationException("The plan or current stage changed. Review the refreshed progress and try again.");
                if (action != "skip" && action != "set" && action != "restart")
                    throw new InvalidOperationException("Unknown stage action.");

                if (action != "set")
                    stageIndex = observed["activeStageIndex"].AsInt;
                if (stageIndex < 0 || stageIndex >= plan.Stages.Length)
                    throw new InvalidOperationException("That stage is no longer available. Reload the plan and try again.");

                var args = new JSONObject();
                args["name"] = name;
                args["expectedRevision"] = plan.RuntimeRevision;
                args["expectedEntry"] = entry;
                args["action"] = action;
                args["stageIndex"] = stageIndex;
                current = Main.reference?.GetCustomPlanProgress();
                if (!CustomStageActionsReady(current) || current["stageEntry"].Value != entry
                    || _customPlanSelector.SelectedItem?.ToString() != name)
                    throw new InvalidOperationException("The selected running plan or current stage changed before submitting. No stage action was sent.");
                SetCustomStatus(Main.reference.OverrideCustomStage(args));
            }
            catch (Exception error)
            {
                SetCustomStatus("Stage action rejected: " + error.Message, true);
            }
            finally
            {
                _customStageOverridePending = false;
                UpdateCustomRebirthDashboard(_customStageSummaryPlan);
            }
        }

        private static string CustomStageRuntimeDescription(JSONNode stage)
        {
            if (stage == null || !stage.IsObject)
                return "Runtime timing is unavailable for this row; no applied or wall time is inferred.";

            Func<string, string> metric = key => stage[key].IsNumber && stage[key].AsDouble >= 0d
                && !double.IsInfinity(stage[key].AsDouble) && !double.IsNaN(stage[key].AsDouble)
                    ? stage[key].AsDouble.ToString("0.###", CultureInfo.InvariantCulture) : "not reported";
            var state = stage["state"].IsString ? stage["state"].Value : "not reported";
            return $"Actual applied time: {metric("appliedSeconds")} s; wall time selected: {metric("elapsedSeconds")} s; visits: {metric("visits")}; state (including skipped): {state}.";
        }

        private void SetCustomStageRuntimeDescription(Control control, string description)
        {
            if (_customStageToolTip == null)
                _customStageToolTip = new ToolTip(components ?? (components = new System.ComponentModel.Container())) {ShowAlways = true};
            if (_customStageToolTip.GetToolTip(control) != description)
                _customStageToolTip.SetToolTip(control, description);
            control.AccessibleDescription = description;
            foreach (Control child in control.Controls)
                SetCustomStageRuntimeDescription(child, description);
        }

        private void UpdateCustomStageRuntimeDetails(JSONObject progress)
        {
            var selected = _customPlanSelector?.SelectedItem as string;
            var matches = selected != null && Main.Settings?.CustomPlanFile == selected
                          && progress != null && progress["available"].AsBool && progress["planName"].Value == selected
                          && !string.IsNullOrEmpty(_customStageRowsRevision) && progress["planRevision"].Value == _customStageRowsRevision
                          && progress["stages"].IsArray && progress["stages"].Count == _customStageRows.Count;
            _customStageActionProgress = matches ? progress : null;
            var ready = !_customStageOverridePending && CustomStageActionsReady(_customStageActionProgress);
            if (_customStageSkipButton != null)
                _customStageSkipButton.Enabled = ready;
            if (_customStageRestartButton != null)
                _customStageRestartButton.Enabled = ready;
            for (var i = 0; i < _customStageRows.Count; i++)
            {
                var row = _customStageRows[i];
                var reported = matches && progress["stages"][i]["index"].IsNumber
                               && progress["stages"][i]["index"].AsDouble == i ? progress["stages"][i] : null;
                var description = CustomStageRuntimeDescription(reported);
                SetCustomStageRuntimeDescription(row.Container, description);
                row.SetCurrentButton.Enabled = ready;
                row.SetCurrentButton.AccessibleName = $"Set current stage to step {i + 1}";
                row.SetCurrentButton.AccessibleDescription = description + " Takes effect immediately; jumping forward bypasses required work and automatic rebirth may happen earlier.";
            }
        }

        private void LoadCustomAdventureIntoUi(SavedSettings settings)
        {
            if (_customAdventureEnabled == null)
                return;

            _customAdventureEnabled.Checked = settings.CustomAdventureEnabled;
            _customAdventureGoldPush.Checked = settings.CustomAdventureGoldPush;
            _customAdventureMaxItopodDeaths.Value = settings.CustomAdventureMaxItopodDeaths;
            _customAdventureMode.SelectedIndex = Math.Max(0, Math.Min(_customAdventureMode.Items.Count - 1, settings.CustomAdventureMode));

            if (_customAdventureZone.DataSource == null && ZoneList != null)
            {
                _customAdventureZone.DataSource = new BindingSource(ZoneList.Where(x => x.Key >= 0).ToList(), null);
                _customAdventureZone.ValueMember = "Key";
                _customAdventureZone.DisplayMember = "Value";
            }

            if (_customAdventureZone.DataSource != null)
            {
                _customAdventureZone.SelectedValue = ZoneList != null && ZoneList.ContainsKey(settings.CustomAdventureZone)
                    ? settings.CustomAdventureZone
                    : 0;
            }
        }

        private void SaveCustomAdventureFromUi()
        {
            if (_initializing || _customPlanInitializing)
                return;

            var zone = Main.Settings.CustomAdventureZone;
            if (_customAdventureZone.SelectedValue is int selectedZone)
            {
                zone = selectedZone;
            }

            Main.Settings.SetSaveDisabled(true);
            try
            {
                Main.Settings.CustomAdventureEnabled = _customAdventureEnabled.Checked;
                Main.Settings.CustomAdventureGoldPush = _customAdventureGoldPush.Checked;
                Main.Settings.CustomAdventureMaxItopodDeaths = (int)_customAdventureMaxItopodDeaths.Value;
                Main.Settings.CustomAdventureMode = Math.Max(0, _customAdventureMode.SelectedIndex);
                Main.Settings.CustomAdventureZone = zone;
            }
            finally
            {
                Main.Settings.SetSaveDisabled(false);
            }

            Main.Settings.SaveSettings();
            SetCustomStatus("Saved Custom adventure goal.");
        }

        private void LoadManualAdventureIntoUi(SavedSettings settings)
        {
            if (_manualAdventureEnabled == null)
                return;

            _manualAdventureEnabled.Checked = settings.ManualAdventureEnabled;
            _manualAdventureBossOnly.Checked = settings.ManualAdventureBossOnly;
            _manualAdventureRecoverHealth.Checked = settings.ManualAdventureRecoverHealth;
            _manualAdventurePrecastBuffs.Checked = settings.ManualAdventurePrecastBuffs;
            _manualAdventureFastCombat.Checked = settings.ManualAdventureFastCombat;
            _manualAdventureBeastMode.Checked = settings.ManualAdventureBeastMode;
            _manualAdventureAllowFallback.Checked = settings.ManualAdventureAllowFallback;

            if (_manualAdventureZone.DataSource == null && ZoneList != null)
            {
                _manualAdventureZone.DataSource = new BindingSource(ZoneList.Where(x => x.Key >= 0).ToList(), null);
                _manualAdventureZone.ValueMember = "Key";
                _manualAdventureZone.DisplayMember = "Value";
            }

            if (_manualAdventureZone.DataSource != null)
            {
                _manualAdventureZone.SelectedValue = ZoneList != null && ZoneList.ContainsKey(settings.ManualAdventureZone)
                    ? settings.ManualAdventureZone
                    : 0;
            }

            if (_manualAdventureStatus != null)
                _manualAdventureStatus.Text = Main.GetCustomAutomationStatus();
        }

        private void SaveManualAdventureFromUi()
        {
            if (_initializing || _customPlanInitializing)
                return;

            var zone = Main.Settings.ManualAdventureZone;
            if (_manualAdventureZone.SelectedValue is int selectedZone)
            {
                zone = selectedZone;
            }

            Main.Settings.SetSaveDisabled(true);
            try
            {
                Main.Settings.ManualAdventureEnabled = _manualAdventureEnabled.Checked;
                Main.Settings.ManualAdventureZone = zone;
                Main.Settings.ManualAdventureBossOnly = _manualAdventureBossOnly.Checked;
                Main.Settings.ManualAdventureRecoverHealth = _manualAdventureRecoverHealth.Checked;
                Main.Settings.ManualAdventurePrecastBuffs = _manualAdventurePrecastBuffs.Checked;
                Main.Settings.ManualAdventureFastCombat = _manualAdventureFastCombat.Checked;
                Main.Settings.ManualAdventureBeastMode = _manualAdventureBeastMode.Checked;
                Main.Settings.ManualAdventureAllowFallback = _manualAdventureAllowFallback.Checked;
            }
            finally
            {
                Main.Settings.SetSaveDisabled(false);
            }

            Main.Settings.SaveSettings();
            SetCustomStatus(Main.Settings.ManualAdventureEnabled ? "Manual adventure fighting enabled." : "Manual adventure fighting disabled.");
        }

        private void LoadCustomFocusSettingsIntoUi(SavedSettings settings)
        {
            if (_customAugmentsBloodEstimateSeconds == null)
                return;

            _customAugmentsBloodEstimateSeconds.Value = Math.Min(
                _customAugmentsBloodEstimateSeconds.Maximum,
                Math.Max(_customAugmentsBloodEstimateSeconds.Minimum, settings.CustomAugmentsBloodEstimateSeconds));
        }

        private void SaveCustomFocusSettingsFromUi()
        {
            if (_initializing || _customPlanInitializing)
                return;

            Main.Settings.CustomAugmentsBloodEstimateSeconds = decimal.ToInt32(_customAugmentsBloodEstimateSeconds.Value);
            SetCustomStatus("Saved Custom focus settings.");
        }

        private void LoadCustomTogglesIntoUi(SavedSettings settings)
        {
            if (_customAutoBoost == null)
                return;

            _customAutoBoost.Checked = settings.CustomAutoBoostEnabled;
            var ratio = settings.CustomAutoBoostCubePowerRatio;
            _customAutoBoostCubeRatio.SelectedValue = ratio >= 1 && ratio <= 4 ? ratio : 2;
            _customAutoMoneyPit.Checked = settings.CustomAutoMoneyPitEnabled;
        }

        private void LoadCustomBloodSettingsIntoUi(CustomPlan plan)
        {
            if (_customAutoBloodSpells == null)
                return;

            var planChanged = _customBloodPlanFile != Main.Settings.CustomPlanFile;
            _customBloodPlanFile = Main.Settings.CustomPlanFile;
            _customAutoBloodSpells.Checked = Main.EffectiveCustomAutoBloodSpellsEnabled(plan);
            _customAutoIronPill.Checked = plan.AutoIronPillEnabled;
            _customBloodSpaghettiGoalPercent.Value = Math.Min(
                _customBloodSpaghettiGoalPercent.Maximum,
                Math.Max(_customBloodSpaghettiGoalPercent.Minimum, Main.EffectiveCustomBloodSpaghettiGoalPercent(plan)));
            _customBloodCounterfeitGoalPercent.Value = Math.Min(
                _customBloodCounterfeitGoalPercent.Maximum,
                Math.Max(_customBloodCounterfeitGoalPercent.Minimum, Main.EffectiveCustomBloodCounterfeitGoalPercent(plan)));
            if (planChanged || !_customIronPillSaveSeconds.Focused)
                _customIronPillSaveSeconds.Value = Math.Min(_customIronPillSaveSeconds.Maximum,
                    Math.Max(_customIronPillSaveSeconds.Minimum, Main.EffectiveCustomIronPillSaveSeconds(plan)));
            if (planChanged || !_customIronPillMinWorth.Focused)
            {
                _customIronPillMinWorth.Text = Main.EffectiveCustomIronPillMinWorth(plan).ToString("R", CultureInfo.InvariantCulture);
                _customIronPillMinWorthLoadedText = _customIronPillMinWorth.Text;
                moneyPitError.SetError(_customIronPillMinWorth, string.Empty);
            }
        }

        private void CustomBloodControl_Changed(object sender, EventArgs e)
        {
            if (_initializing || _customPlanInitializing || Main.CustomPlanStore == null)
                return;

            RunCustomUiAction("Save selected plan Blood Magic", () =>
            {
                var planName = Main.Settings.CustomPlanFile;
                var plan = Main.CustomPlanStore.Load(planName);
                // Only the edited control creates an override; displayed inherited values stay unset.
                if (sender == _customAutoBloodSpells)
                    plan.AutoBloodSpellsEnabled = _customAutoBloodSpells.Checked;
                else if (sender == _customAutoIronPill)
                    plan.AutoIronPillEnabled = _customAutoIronPill.Checked;
                else if (sender == _customBloodSpaghettiGoalPercent)
                    plan.BloodSpaghettiGoalPercent = decimal.ToInt32(_customBloodSpaghettiGoalPercent.Value);
                else if (sender == _customBloodCounterfeitGoalPercent)
                    plan.BloodCounterfeitGoalPercent = decimal.ToInt32(_customBloodCounterfeitGoalPercent.Value);
                else if (sender == _customIronPillSaveSeconds)
                    plan.IronPillSaveSeconds = decimal.ToInt32(_customIronPillSaveSeconds.Value);
                else if (sender == _customIronPillMinWorth)
                {
                    var worth = double.Parse(_customIronPillMinWorth.Text, NumberStyles.Float, CultureInfo.InvariantCulture);
                    // Validation also runs on focus changes without an edit.
                    if (worth == Main.EffectiveCustomIronPillMinWorth(plan))
                    {
                        _customIronPillMinWorthLoadedText = _customIronPillMinWorth.Text;
                        return;
                    }
                    plan.IronPillMinWorth = worth;
                }
                else
                    return;

                Main.CustomPlanStore.Save(planName, plan);
                if (sender == _customIronPillMinWorth)
                    _customIronPillMinWorthLoadedText = _customIronPillMinWorth.Text;
                _customStageRowsRevision = plan.RuntimeRevision;
                UpdateCustomRebirthDashboard(plan);
                UpdateCustomBloodGoalEstimates();
                SetCustomStatus($"Saved Blood Magic for '{planName}'.");
            }, false);
        }

        private void SaveCustomTogglesFromUi()
        {
            if (_initializing || _customPlanInitializing)
                return;

            var ratio = _customAutoBoostCubeRatio.SelectedValue is int selectedRatio ? selectedRatio : 2;
            Main.Settings.SetSaveDisabled(true);
            try
            {
                Main.Settings.CustomAutoBoostEnabled = _customAutoBoost.Checked;
                Main.Settings.CustomAutoBoostCubePowerRatio = ratio;
                Main.Settings.CustomAutoMoneyPitEnabled = _customAutoMoneyPit.Checked;
            }
            finally
            {
                Main.Settings.SetSaveDisabled(false);
            }

            Main.Settings.SaveSettings();
            SetCustomStatus("Saved Custom toggles.");
        }

        private void CustomAdventureControl_Changed(object sender, EventArgs e)
        {
            RunCustomUiAction("Save custom adventure goal", SaveCustomAdventureFromUi, false);
        }

        private void ManualAdventureControl_Changed(object sender, EventArgs e)
        {
            RunCustomUiAction("Save manual adventure", SaveManualAdventureFromUi, false);
        }

        private void CustomFocusSettingsControl_Changed(object sender, EventArgs e)
        {
            RunCustomUiAction("Save custom focus settings", SaveCustomFocusSettingsFromUi, false);
        }

        private void CustomTogglesControl_Changed(object sender, EventArgs e)
        {
            RunCustomUiAction("Save custom toggles", SaveCustomTogglesFromUi, false);
        }

        private void CustomUsePlan_CheckedChanged(object sender, EventArgs e)
        {
            if (_customPlanInitializing) return;
            RunCustomUiAction("Toggle custom rebirth plan", () =>
            {
                Main.Settings.UseCustomRebirthPlan = _customUsePlan.Checked;
                RefreshCustomRebirthDashboardFromCurrentPlan();
                SetCustomStatus(_customUsePlan.Checked ? "Selected Custom plan is running from the current rebirth time." : "Selected Custom plan stopped.");
            }, false);
        }

        private void CustomGlobalAutoRebirth_CheckedChanged(object sender, EventArgs e)
        {
            if (_customPlanInitializing) return;
            RunCustomUiAction("Toggle custom auto-rebirth", () =>
            {
                Main.Settings.AutoRebirth = _customGlobalAutoRebirth.Checked;
                if (_customGlobalAutoRebirth.Checked && !Main.Settings.UseCustomRebirthPlan)
                {
                    Main.Settings.UseCustomRebirthPlan = true;
                    _customPlanInitializing = true;
                    try
                    {
                        _customUsePlan.Checked = true;
                    }
                    finally
                    {
                        _customPlanInitializing = false;
                    }
                }

                RefreshCustomRebirthDashboardFromCurrentPlan();
                SetCustomStatus(_customGlobalAutoRebirth.Checked ? "Custom auto-rebirth enabled for the selected plan." : "Custom auto-rebirth disabled; stages can still run if the plan is running.");
            }, false);
        }

        private void RefreshCustomRebirthDashboardFromCurrentPlan()
        {
            if (Main.CustomPlanStore == null)
                return;

            UpdateCustomRebirthDashboard(Main.CustomPlanStore.Load(Main.Settings.CustomPlanFile));
        }

        private void CustomPlanControl_Changed(object sender, EventArgs e)
        {
            RunCustomUiAction("Auto-save custom plan", SaveCustomPlanFromUi, false);
        }

        private void CustomPlanSelector_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_customPlanInitializing || _customPlanSelector.SelectedItem == null) return;
            RunCustomUiAction("Select custom plan", () =>
            {
                var selected = _customPlanSelector.SelectedItem.ToString();
                Main.Settings.CustomPlanFile = selected;
                _customPlanInitializing = true;
                try
                {
                    LoadCustomPlanIntoUi(Main.CustomPlanStore.Load(selected));
                }
                finally
                {
                    _customPlanInitializing = false;
                }
            }, false);
        }

        private void CustomGoalPresetSelector_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_customPlanInitializing || _customGoalPresetSelector.SelectedIndex < 0) return;

            RunCustomUiAction("Select goal preset", () =>
            {
                var planName = Main.Settings.CustomPlanFile;
                var plan = Main.CustomPlanStore.Load(planName);
                plan.ActiveGoalPresetIndex = _customGoalPresetSelector.SelectedIndex;
                Main.CustomPlanStore.Save(planName, plan);

                _customPlanInitializing = true;
                try
                {
                    LoadGoalPresetControls(plan);
                }
                finally
                {
                    _customPlanInitializing = false;
                }

                SetCustomStatus($"Loaded goal preset '{plan.GoalPresets[plan.ActiveGoalPresetIndex].Name}'");
            }, false);
        }

        private void CustomPlanNew_Click(object sender, EventArgs e)
        {
            var name = PromptForPlanName("New custom plan", "Plan name", "new-plan");
            if (string.IsNullOrWhiteSpace(name)) return;
            Main.Settings.CustomPlanFile = Main.CustomPlanStore.CreatePlan(name);
        }

        private void CustomPlanCopy_Click(object sender, EventArgs e)
        {
            var current = Main.CustomPlanStore.Load(Main.Settings.CustomPlanFile);
            var name = PromptForPlanName("Copy custom plan", "Plan name", current.Name + " copy");
            if (string.IsNullOrWhiteSpace(name)) return;
            Main.Settings.CustomPlanFile = Main.CustomPlanStore.CreatePlan(name, current);
        }

        private void CustomPlanDelete_Click(object sender, EventArgs e)
        {
            var current = Main.Settings.CustomPlanFile;
            var result = MessageBox.Show($"Delete custom plan '{current}'?", "Delete Custom Plan", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (result != DialogResult.Yes) return;
            try { Main.Settings.CustomPlanFile = Main.CustomPlanStore.DeleteExternalPlan(current); }
            catch (Exception error) { SetCustomStatus(error.Message); }
        }

        private void CustomGoalPresetNew_Click(object sender, EventArgs e)
        {
            var name = PromptForPlanName("New goal preset", "Preset name", "new goal preset");
            if (string.IsNullOrWhiteSpace(name)) return;

            var planName = Main.Settings.CustomPlanFile;
            var plan = Main.CustomPlanStore.Load(planName);
            var presets = plan.GoalPresets.ToList();
            var goal = _customGearGoal.SelectedValue is int selected ? selected : 0;
            presets.Add(new CustomGoalLoadoutPreset {Name = name.Trim(), GearGoal = goal});
            plan.GoalPresets = presets.ToArray();
            plan.ActiveGoalPresetIndex = plan.GoalPresets.Length - 1;
            Main.CustomPlanStore.Save(planName, plan);
            ReloadGoalPresetUi(planName);
        }

        private void CustomGoalPresetCopy_Click(object sender, EventArgs e)
        {
            var planName = Main.Settings.CustomPlanFile;
            var plan = Main.CustomPlanStore.Load(planName);
            var active = plan.GoalPresets[plan.ActiveGoalPresetIndex];
            var name = PromptForPlanName("Copy goal preset", "Preset name", active.Name + " copy");
            if (string.IsNullOrWhiteSpace(name)) return;

            var presets = plan.GoalPresets.ToList();
            presets.Add(new CustomGoalLoadoutPreset {Name = name.Trim(), GearGoal = active.GearGoal});
            plan.GoalPresets = presets.ToArray();
            plan.ActiveGoalPresetIndex = plan.GoalPresets.Length - 1;
            Main.CustomPlanStore.Save(planName, plan);
            ReloadGoalPresetUi(planName);
        }

        private void CustomGoalPresetDelete_Click(object sender, EventArgs e)
        {
            var planName = Main.Settings.CustomPlanFile;
            var plan = Main.CustomPlanStore.Load(planName);
            if (plan.GoalPresets.Length <= 1)
            {
                SetCustomStatus("At least one goal preset is required.", true);
                return;
            }

            var active = plan.GoalPresets[plan.ActiveGoalPresetIndex];
            var result = MessageBox.Show($"Delete goal preset '{active.Name}'?", "Delete Goal Preset", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (result != DialogResult.Yes) return;

            var presets = plan.GoalPresets.ToList();
            presets.RemoveAt(plan.ActiveGoalPresetIndex);
            plan.GoalPresets = presets.ToArray();
            plan.ActiveGoalPresetIndex = Math.Min(plan.ActiveGoalPresetIndex, plan.GoalPresets.Length - 1);
            Main.CustomPlanStore.Save(planName, plan);
            ReloadGoalPresetUi(planName);
        }

        private void CustomUseNumberClimbStages_Click(object sender, EventArgs e)
        {
            _customPlanInitializing = true;
            try
            {
                _customStageSchedulerEnabled.Checked = true;
                LoadCustomStageRows(CustomPlan.DefaultNumberClimbStages());
                _customStageHours.Value = 0;
                _customStageMinutes.Value = 30;
                _customStageSeconds.Value = 0;
            }
            finally
            {
                _customPlanInitializing = false;
            }

            SaveCustomPlanFromUi();
        }

        private void CustomUseAdvancedTrainingNguStages_Click(object sender, EventArgs e)
        {
            var stages = new[]
            {
                new CustomPlanStage
                {
                    Name = "Advanced Training to 6k/6k",
                    Percent = 100.0,
                    EnergyFocus = (int) CustomStageFocus.AdvancedTraining,
                    MagicFocus = (int) CustomStageFocus.BloodMagic,
                    GearGoal = (int) GearOptimizationGoal.AdvancedTraining,
                    CompletionMode = (int) CustomStageCompletionMode.AdvancedTrainingStats,
                    TargetAdvancedTrainingPower = 6000,
                    TargetAdvancedTrainingToughness = 6000
                },
                new CustomPlanStage
                {
                    Name = "NGU sustain",
                    Percent = 100.0,
                    EnergyFocus = (int) CustomStageFocus.EnergyNgu,
                    MagicFocus = (int) CustomStageFocus.MagicNgu,
                    GearGoal = (int) GearOptimizationGoal.EnergyNgu,
                    CompletionMode = (int) CustomStageCompletionMode.TimePercent
                }
            };

            _customPlanInitializing = true;
            try
            {
                _customStageSchedulerEnabled.Checked = true;
                LoadCustomStageRows(stages);
                if (_customStageHours.Value == 0 && _customStageMinutes.Value == 0 && _customStageSeconds.Value == 0)
                    _customStageHours.Value = 1;
            }
            finally
            {
                _customPlanInitializing = false;
            }

            SaveCustomPlanFromUi();
        }

        private void CustomUseBuiltInPlan_Click(string planName, bool autoRebirth)
        {
            if (Main.CustomPlanStore == null)
            {
                SetCustomStatus("Custom plan store is not ready.", true);
                return;
            }

            Main.CustomPlanStore.ListPlanNames();
            Main.Settings.SetSaveDisabled(true);
            try
            {
                Main.Settings.CustomPlanFile = planName;
                Main.Settings.UseCustomRebirthPlan = true;
                Main.Settings.AutoRebirth = autoRebirth;
            }
            finally
            {
                Main.Settings.SetSaveDisabled(false);
            }

            Main.Settings.SaveSettings();

            _customPlanInitializing = true;
            try
            {
                _customUsePlan.Checked = Main.Settings.UseCustomRebirthPlan;
                _customGlobalAutoRebirth.Checked = Main.Settings.AutoRebirth;
                RefreshCustomPlanSelector(planName);
                LoadCustomPlanIntoUi(Main.CustomPlanStore.Load(planName));
            }
            finally
            {
                _customPlanInitializing = false;
            }

            SetCustomStatus(autoRebirth
                ? $"Selected {planName}, started the Custom plan, and enabled Custom auto-rebirth."
                : $"Selected {planName}, started the Custom plan, and disabled Custom auto-rebirth.");
        }

        private void CustomOptimizeGear_Click(object sender, EventArgs e)
        {
            var goal = _customGearGoal.SelectedValue is int selected ? selected : 0;
            var fightScoreMode = _customFightScoreMode.SelectedValue is int selectedMode
                ? (FightScoreMode) selectedMode
                : FightScoreMode.SqrtPowerToughness;
            var result = Main.reference.ApplyManualGearSelectionIngress((GearOptimizationGoal) goal, fightScoreMode);
            SetCustomStatus(result.Message, !result.Success);
            if (result.Success)
            {
                Main.Log(result.Message);
            }
        }

        private void CustomFocusGoal_Click(CustomFocusGoal goal)
        {
            if (Main.reference == null)
            {
                SetCustomStatus("Injector instance is not ready.", true);
                return;
            }

            var result = Main.reference.ToggleCustomFocusGoal(goal);
            _customPlanInitializing = true;
            try
            {
                _customGlobalAutoRebirth.Checked = Main.Settings.AutoRebirth;
                _customUsePlan.Checked = Main.Settings.UseCustomRebirthPlan;
                LoadCustomAdventureIntoUi(Main.Settings);
            }
            finally
            {
                _customPlanInitializing = false;
            }

            UpdateCustomFocusButtonHighlights();
            SetCustomStatus(result);
        }

        private void CustomPreviewAugment_Click(object sender, EventArgs e)
        {
            var result = AugmentOptimizer.OptimizeBest(CustomAugmentsBloodEstimateSeconds());
            var message = FormatAugmentPreview(result);
            if (_customAugmentPreview != null)
                _customAugmentPreview.Text = message;
            SetCustomStatus(message, !result.Success);
            if (result.Success)
            {
                Main.Log(message);
            }
        }

        private void CustomAllocateAugment_Click(object sender, EventArgs e)
        {
            var result = Main.reference.RequestAugmentAllocation(AugmentOptimizer.OptimizeBest(CustomAugmentsBloodEstimateSeconds()));
            var message = FormatAugmentPreview(result);
            if (_customAugmentPreview != null)
                _customAugmentPreview.Text = message;
            SetCustomStatus(message, !result.Success);
            if (result.Success)
            {
                Main.Log(message);
            }
        }

        private static string FormatAugmentPreview(AugmentOptimizerResult result)
        {
            if (result == null)
                return "No augment result.";

            if (!result.Success)
                return result.Message;

            return string.Join(Environment.NewLine, new[]
            {
                result.Message,
                $"Energy: aug {result.EnergyToAugment:0}, upgrade {result.EnergyToUpgrade:0}; energy power {result.EnergyPower:0.###}; augment multiplier {result.AugmentSpeedMultiplier:0.###}.",
                $"Expected work: {result.ProjectedWork:0.###} over {result.ExpectedSeconds:0.#}s; expected value {result.ExpectedValue:0.###}; score {result.ValuePerSecond:0.###}/s.",
                $"Gold: projected cost {result.ProjectedGoldCost:0.###}; budget {result.ProjectedGoldBudget:0.###}; net GPS {result.GoldPerSecond:0.###}."
            });
        }

        private static int CustomAugmentsBloodEstimateSeconds()
        {
            return Main.Settings == null ? 300 : Main.Settings.CustomAugmentsBloodEstimateSeconds;
        }

        private void CustomPreCacheLoadouts_Click(object sender, EventArgs e)
        {
            if (Main.reference == null)
            {
                SetCustomStatus("Injector instance is not ready.", true);
                return;
            }

            SetCustomStatus(Main.reference.PreCacheCustomEquipmentLoadouts());
        }

        private void LoadGoalPresetsIntoUi(CustomPlan plan)
        {
            if (_customGoalPresetSelector != null)
            {
                _customGoalPresetSelector.Items.Clear();
                _customGoalPresetSelector.Items.AddRange(plan.GoalPresets.Select(x => x.Name).ToArray());
                _customGoalPresetSelector.SelectedIndex = plan.ActiveGoalPresetIndex;
            }

            LoadGoalPresetControls(plan);
        }

        private void LoadGoalPresetControls(CustomPlan plan)
        {
            var active = plan.GoalPresets[plan.ActiveGoalPresetIndex];
            if (_customGoalPresetName != null)
            {
                _customGoalPresetName.Text = active.Name;
            }

            _customGearGoal.SelectedValue = GearOptimizer.GoalLabels.ContainsKey(active.GearGoal) ? active.GearGoal : 0;
        }

        private void SaveGoalPresetFromUi(CustomPlan plan)
        {
            var index = Math.Max(0, Math.Min(plan.ActiveGoalPresetIndex, plan.GoalPresets.Length - 1));
            plan.ActiveGoalPresetIndex = index;
            var active = plan.GoalPresets[index];
            active.Name = _customGoalPresetName == null || string.IsNullOrWhiteSpace(_customGoalPresetName.Text) ? "Generated" : _customGoalPresetName.Text.Trim();
            active.GearGoal = _customGearGoal.SelectedValue is int goal ? goal : 0;
            plan.GearGoal = active.GearGoal;

            if (_customGoalPresetSelector != null && index < _customGoalPresetSelector.Items.Count && _customGoalPresetSelector.Items[index].ToString() != active.Name)
            {
                _customGoalPresetSelector.Items[index] = active.Name;
            }
        }

        private void LoadCustomStageRows(CustomPlanStage[] stages)
        {
            if (_customStageSteps == null)
                return;

            _customStageRowsInitializing = true;
            try
            {
                _customStageRowsRevision = null;
                UpdateCustomStageRuntimeDetails(null);
                _customStageToolTip?.RemoveAll();
                foreach (Control control in _customStageSteps.Controls.Cast<Control>().ToArray())
                    control.Dispose();
                _customStageRows.Clear();
                _customStageSteps.Controls.Clear();
                var safeStages = stages == null || stages.Length == 0
                    ? new[] {NewDefaultCustomStage()}
                    : stages;

                for (var i = 0; i < safeStages.Length; i++)
                {
                    var row = CreateCustomStageRow(safeStages[i] ?? new CustomPlanStage {Name = $"Step {i + 1}"}, i, safeStages.Length);
                    _customStageRows.Add(row);
                    _customStageSteps.Controls.Add(row.Container);
                }
            }
            finally
            {
                _customStageRowsInitializing = false;
            }

            RefreshStageRowPreviews();
        }

        private CustomStageEditorRow CreateCustomStageRow(CustomPlanStage stage, int index, int count)
        {
            var row = new CustomStageEditorRow();
            row.SourceStage = CloneStage(stage);
            row.Container = new GroupBox
            {
                Text = $"Step {index + 1}",
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Top,
                Padding = new Padding(10, 8, 10, 10),
                Margin = new Padding(0, 4, 0, 8),
                MinimumSize = new Size(CustomWideEditorMinWidth, 0)
            };

            var layout = NewTwoColumnLayout();
            layout.ColumnStyles[0].Width = 76;
            layout.ColumnStyles[1].Width = 100;

            var header = NewInlineFlowPanel();
            row.DragHandle = new Label
            {
                Text = "Drag",
                AutoSize = true,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.Gainsboro,
                Cursor = Cursors.SizeAll,
                Padding = new Padding(8, 7, 8, 6),
                Margin = new Padding(3, 4, 8, 4)
            };
            row.DragHandle.Tag = row;
            row.DragHandle.MouseDown += CustomStageDragHandle_MouseDown;
            header.Controls.Add(row.DragHandle);

            row.Name = NewTextBox(270);
            row.Name.Text = string.IsNullOrWhiteSpace(stage.Name) ? $"Step {index + 1}" : stage.Name;
            row.Name.TextChanged += CustomStageRow_Changed;
            header.Controls.Add(row.Name);
            row.DeleteButton = NewSmallButton("Delete", (sender, args) => DeleteCustomStageRow(row));
            header.Controls.Add(row.DeleteButton);
            row.SetCurrentButton = NewSmallButton("Set current", (sender, args) => CustomOverrideStage("set", _customStageRows.IndexOf(row)));
            row.SetCurrentButton.Enabled = false;
            row.SetCurrentButton.AccessibleName = $"Set current stage to step {index + 1}";
            header.Controls.Add(row.SetCurrentButton);
            layout.Controls.Add(NewLabel("Name"), 0, 0);
            layout.Controls.Add(header, 1, 0);

            var action = NewInlineFlowPanel();
            action.Controls.Add(NewInlineLabel("Preset"));
            row.Preset = NewComboBox(210);
            BindCombo(row.Preset, StagePresetLabels().ToList(), DetectStagePreset(stage), null);
            row.Preset.SelectedIndexChanged += (sender, args) =>
            {
                UpdateStageRowPresetDetails(row);
                CustomStageRow_Changed(sender, args);
            };
            action.Controls.Add(row.Preset);
            row.Details = new Label
            {
                AutoSize = true,
                MaximumSize = new Size(610, 0),
                ForeColor = Color.DimGray,
                Padding = new Padding(4, 7, 8, 0),
                Margin = new Padding(0, 3, 0, 3)
            };
            action.Controls.Add(row.Details);
            layout.Controls.Add(NewLabel("Preset"), 0, 1);
            layout.Controls.Add(action, 1, 1);

            var until = NewInlineFlowPanel();
            row.CompletionMode = NewComboBox(240);
            BindCombo(row.CompletionMode, StageCompletionModeLabels().ToList(), stage.CompletionMode, null);
            row.CompletionMode.SelectedIndexChanged += (sender, args) =>
            {
                UpdateStageRowConditionVisibility(row);
                CustomStageRow_Changed(sender, args);
            };
            until.Controls.Add(row.CompletionMode);

            row.FixedSecondsLabel = NewInlineLabel("fixed seconds (0 = flex)");
            until.Controls.Add(row.FixedSecondsLabel);
            row.FixedSeconds = new NumericUpDown
            {
                Minimum = 0,
                Maximum = int.MaxValue,
                Width = 100,
                Margin = new Padding(3, 4, 6, 4),
                Value = Math.Max(0, stage.DurationSeconds)
            };
            row.FixedSeconds.ValueChanged += (sender, args) =>
            {
                UpdateStageRowConditionVisibility(row);
                CustomStageRow_Changed(sender, args);
            };
            until.Controls.Add(row.FixedSeconds);

            row.PercentLabel = NewInlineLabel("flex weight");
            until.Controls.Add(row.PercentLabel);
            row.Percent = NewTextBox(110);
            row.Percent.Text = stage.Percent.ToString("R", CultureInfo.InvariantCulture);
            row.Percent.TextChanged += CustomStageRow_Changed;
            until.Controls.Add(row.Percent);

            row.TargetPowerLabel = NewInlineLabel("power >=");
            until.Controls.Add(row.TargetPowerLabel);
            row.TargetPower = NewTextBox(78);
            row.TargetPower.MinimumSize = new Size(78, 0);
            row.TargetPower.Text = stage.TargetAdvancedTrainingPower > 0 ? stage.TargetAdvancedTrainingPower.ToString(CultureInfo.InvariantCulture) : "6000";
            row.TargetPower.TextChanged += CustomStageRow_Changed;
            until.Controls.Add(row.TargetPower);

            row.TargetToughnessLabel = NewInlineLabel("toughness >=");
            until.Controls.Add(row.TargetToughnessLabel);
            row.TargetToughness = NewTextBox(78);
            row.TargetToughness.MinimumSize = new Size(78, 0);
            row.TargetToughness.Text = stage.TargetAdvancedTrainingToughness > 0 ? stage.TargetAdvancedTrainingToughness.ToString(CultureInfo.InvariantCulture) : "6000";
            row.TargetToughness.TextChanged += CustomStageRow_Changed;
            until.Controls.Add(row.TargetToughness);

            row.Duration = new Label
            {
                AutoSize = true,
                MaximumSize = new Size(600, 0),
                ForeColor = Color.DarkSlateGray,
                Padding = new Padding(4, 7, 8, 0),
                Margin = new Padding(0, 3, 0, 3)
            };
            until.Controls.Add(row.Duration);

            layout.Controls.Add(NewLabel("Timing"), 0, 2);
            layout.Controls.Add(until, 1, 2);

            row.EnergyPriorities = NewTextBox(440);
            row.EnergyPriorities.Text = string.Join(", ", stage.EnergyPriorities ?? new string[0]);
            row.EnergyPriorities.TextChanged += CustomStageRow_Changed;
            layout.Controls.Add(NewLabel("Energy order"), 0, 3);
            layout.Controls.Add(row.EnergyPriorities, 1, 3);

            row.MagicPriorities = NewTextBox(440);
            row.MagicPriorities.Text = string.Join(", ", stage.MagicPriorities ?? new string[0]);
            row.MagicPriorities.TextChanged += CustomStageRow_Changed;
            layout.Controls.Add(NewLabel("Magic order"), 0, 4);
            layout.Controls.Add(row.MagicPriorities, 1, 4);

            var lookahead = NewInlineFlowPanel();
            row.BlackBarLookaheadSeconds = new NumericUpDown
            {
                Minimum = 3,
                Maximum = int.MaxValue,
                Width = 90,
                Margin = new Padding(3, 4, 6, 4),
                Value = Math.Max(3, stage.BlackBarLookaheadSeconds)
            };
            row.BlackBarLookaheadSeconds.ValueChanged += CustomStageRow_Changed;
            lookahead.Controls.Add(row.BlackBarLookaheadSeconds);
            lookahead.Controls.Add(NewInlineLabel("seconds (minimum 3; refresh every 2 seconds)"));
            layout.Controls.Add(NewLabel("Lookahead"), 0, 5);
            layout.Controls.Add(lookahead, 1, 5);

            var priorityHelp = new Label
            {
                AutoSize = true,
                MaximumSize = new Size(480, 0),
                ForeColor = Color.DimGray,
                Margin = new Padding(3, 4, 3, 6),
                Text = "Comma-separated tokens, highest priority first; blank uses preset defaults. Both: WAN, TM, NGU. Energy: NGU-0..8, AUG, AUG-0..13, AT, AT-0..4. Magic: NGU-0..6, RIT, RIT-0..7. Generic NGU follows the plan's positive energy/magic weights with capped redistribution; zero excludes a target. Edit weights in the desktop controller's Misc page. Explicit NGU-n targets remain ordered. AUG/RIT use pinned optimizer targets; AT uses unfinished stage P/T targets. Custom orders and lookahead are retained when changing presets."
            };
            layout.Controls.Add(priorityHelp, 1, 6);

            var fight = NewInlineFlowPanel();
            row.FightScheduleEnabled = new CheckBox {Text = "Enabled", AutoSize = true, Checked = stage.FightSchedule != null};
            fight.Controls.Add(row.FightScheduleEnabled);
            fight.Controls.Add(NewInlineLabel("Anchor"));
            row.FightAnchor = NewComboBox(240);
            BindCombo(row.FightAnchor, new Dictionary<int, string>
            {
                {0, "Stage entry"}, {1, "First verified Augments funding"}
            }.ToList(), stage.FightSchedule?.Anchor ?? 0, null);
            row.FightAnchor.Enabled = row.FightScheduleEnabled.Checked;
            row.FightAnchor.AccessibleName = "Fight schedule anchor";
            row.FightAnchor.SelectedIndexChanged += CustomStageRow_Changed;
            fight.Controls.Add(row.FightAnchor);
            fight.Controls.Add(NewInlineLabel("Delay (s)"));
            row.FightDelaySeconds = new NumericUpDown
            {
                Minimum = 0, Maximum = int.MaxValue, Width = 100,
                Value = stage.FightSchedule?.DelaySeconds ?? 0,
                Enabled = row.FightScheduleEnabled.Checked, AccessibleName = "Fight delay seconds"
            };
            row.FightDelaySeconds.ValueChanged += CustomStageRow_Changed;
            fight.Controls.Add(row.FightDelaySeconds);
            fight.Controls.Add(NewInlineLabel("Repeat (s, 0 = once)"));
            row.FightIntervalSeconds = new NumericUpDown
            {
                Minimum = 0, Maximum = int.MaxValue, Width = 100,
                Value = stage.FightSchedule?.IntervalSeconds ?? 0,
                Enabled = row.FightScheduleEnabled.Checked, AccessibleName = "Fight repeat interval seconds"
            };
            row.FightIntervalSeconds.ValueChanged += CustomStageRow_Changed;
            fight.Controls.Add(row.FightIntervalSeconds);
            row.FightScheduleEnabled.AccessibleName = "Enable stage fight schedule";
            row.FightScheduleEnabled.CheckedChanged += (sender, args) =>
            {
                row.FightAnchor.Enabled = row.FightDelaySeconds.Enabled = row.FightIntervalSeconds.Enabled = row.FightScheduleEnabled.Checked;
                CustomStageRow_Changed(sender, args);
            };
            layout.Controls.Add(NewLabel("Fight"), 0, 7);
            layout.Controls.Add(fight, 1, 7);
            row.Container.Controls.Add(layout);
            row.DeleteButton.Enabled = count > 1;
            row.Container.Tag = row;
            EnableCustomStageDrop(row.Container);
            UpdateStageRowPresetDetails(row);
            UpdateStageRowConditionVisibility(row);
            SetCustomStageRuntimeDescription(row.Container, CustomStageRuntimeDescription(null));
            return row;
        }

        private void BindCombo(ComboBox combo, List<KeyValuePair<int, string>> items, int selectedValue, EventHandler changed)
        {
            combo.DataSource = new BindingSource(items, null);
            combo.ValueMember = "Key";
            combo.DisplayMember = "Value";
            combo.SelectedValue = items.Any(x => x.Key == selectedValue) ? selectedValue : items[0].Key;
            if (changed != null)
                combo.SelectedIndexChanged += changed;
        }

        private static Dictionary<int, string> StageCompletionModeLabels()
        {
            return new Dictionary<int, string>
            {
                {(int) CustomStageCompletionMode.TimePercent, "Fixed duration / Flexible share"},
                {(int) CustomStageCompletionMode.AdvancedTrainingStats, "AT targets (mandatory)"}
            };
        }

        private static Dictionary<int, string> StageGearGoalLabels()
        {
            var labels = new Dictionary<int, string> {{-1, "Current gear"}};
            foreach (var goal in GearOptimizer.GoalLabels)
            {
                labels[goal.Key] = goal.Value;
            }

            return labels;
        }

        private static Dictionary<int, string> StagePresetLabels()
        {
            return StagePresetDefinitions().ToDictionary(x => x.Id, x => x.Name);
        }

        private static List<CustomStagePresetDefinition> StagePresetDefinitions()
        {
            return new List<CustomStagePresetDefinition>
            {
                new CustomStagePresetDefinition {Id = 0, Name = "Custom", EnergyFocus = (int) CustomStageFocus.None, MagicFocus = (int) CustomStageFocus.None, GearGoal = -1, Details = "Uses the saved custom energy, magic, and gear settings."},
                new CustomStagePresetDefinition {Id = 1, Name = "Time Machine", EnergyFocus = (int) CustomStageFocus.TimeMachine, MagicFocus = (int) CustomStageFocus.TimeMachine, GearGoal = (int) GearOptimizationGoal.TimeMachine, Details = "TM: energy for speed, magic for gold multiplier."},
                new CustomStagePresetDefinition {Id = 2, Name = "Augments + Blood", EnergyFocus = (int) CustomStageFocus.Augments, MagicFocus = (int) CustomStageFocus.BloodMagic, GearGoal = (int) GearOptimizationGoal.Augments, Details = "AUG: pinned optimizer augment, then upgrade if selected. RIT: pinned optimizer Blood Magic ritual."},
                new CustomStagePresetDefinition {Id = 3, Name = "Wandoos", EnergyFocus = (int) CustomStageFocus.Wandoos, MagicFocus = (int) CustomStageFocus.Wandoos, GearGoal = (int) GearOptimizationGoal.Wandoos, Details = "WAN: energy/magic at the Wandoos black-bar cap."},
                new CustomStagePresetDefinition {Id = 4, Name = "Energy NGU", EnergyFocus = (int) CustomStageFocus.EnergyNgu, MagicFocus = (int) CustomStageFocus.MagicNgu, GearGoal = (int) GearOptimizationGoal.EnergyNgu, Details = "Energy/magic: NGUs follow the plan's weights with capped redistribution. Energy-focused gear; saved priority overrides still apply."},
                new CustomStagePresetDefinition {Id = 5, Name = "Magic NGU", EnergyFocus = (int) CustomStageFocus.None, MagicFocus = (int) CustomStageFocus.MagicNgu, GearGoal = (int) GearOptimizationGoal.MagicNgu, Details = "Energy: unchanged. Magic: NGUs follow the plan's weights with capped redistribution; saved priority overrides still apply."},
                new CustomStagePresetDefinition {Id = 6, Name = "All NGU", EnergyFocus = (int) CustomStageFocus.EnergyNgu, MagicFocus = (int) CustomStageFocus.MagicNgu, GearGoal = (int) GearOptimizationGoal.AllNgu, Details = "Energy/magic: NGUs follow the plan's weights with capped redistribution. Balanced NGU gear; saved priority overrides still apply."},
                new CustomStagePresetDefinition {Id = 7, Name = "Adventure Stats", EnergyFocus = (int) CustomStageFocus.AdvancedTraining, MagicFocus = (int) CustomStageFocus.BloodMagic, GearGoal = (int) GearOptimizationGoal.AdvancedTraining, Details = "AT: power then toughness; P/T completion skips reached stage targets. RIT: pinned optimizer Blood Magic ritual."}
            };
        }

        private static CustomStagePresetDefinition StagePresetDefinition(int presetId)
        {
            return StagePresetDefinitions().FirstOrDefault(x => x.Id == presetId) ?? StagePresetDefinitions()[0];
        }

        private static int DetectStagePreset(CustomPlanStage stage)
        {
            if (stage == null)
                return 0;

            foreach (var preset in StagePresetDefinitions().Where(x => x.Id != 0))
            {
                if (stage.EnergyFocus == preset.EnergyFocus && stage.MagicFocus == preset.MagicFocus && stage.GearGoal == preset.GearGoal)
                    return preset.Id;
            }

            return 0;
        }

        private static CustomPlanStage CloneStage(CustomPlanStage stage)
        {
            stage = stage ?? NewDefaultCustomStage();
            return new CustomPlanStage
            {
                SourceJson = stage.SourceJson == null ? null : JsonFiles.ParseObject(stage.SourceJson.ToString()),
                Name = stage.Name,
                Percent = stage.Percent,
                DurationSeconds = stage.DurationSeconds,
                DurationUnit = stage.DurationUnit,
                EnergyFocus = stage.EnergyFocus,
                MagicFocus = stage.MagicFocus,
                UseWandoosWhenTimeMachineLocked = stage.UseWandoosWhenTimeMachineLocked,
                ExecutionMode = stage.ExecutionMode,
                PresetId = stage.PresetId,
                EnergyPriorities = stage.EnergyPriorities == null ? null : (string[]) stage.EnergyPriorities.Clone(),
                MagicPriorities = stage.MagicPriorities == null ? null : (string[]) stage.MagicPriorities.Clone(),
                BlackBarLookaheadSeconds = stage.BlackBarLookaheadSeconds,
                GearGoal = stage.GearGoal,
                AdventureDynamicWeight = stage.AdventureDynamicWeight,
                FightSchedule = stage.FightSchedule == null ? null : new CustomFightSchedule
                {
                    Id = stage.FightSchedule.Id,
                    Anchor = stage.FightSchedule.Anchor,
                    DelaySeconds = stage.FightSchedule.DelaySeconds,
                    IntervalSeconds = stage.FightSchedule.IntervalSeconds,
                    SourceJson = stage.FightSchedule.SourceJson == null ? null : JsonFiles.ParseObject(stage.FightSchedule.SourceJson.ToString())
                },
                CompletionMode = stage.CompletionMode,
                TargetAdvancedTrainingPower = stage.TargetAdvancedTrainingPower,
                TargetAdvancedTrainingToughness = stage.TargetAdvancedTrainingToughness
            };
        }

        private void UpdateStageRowPresetDetails(CustomStageEditorRow row)
        {
            if (row == null || row.Details == null)
                return;

            var presetId = ReadComboValue(row.Preset, 0);
            row.Details.Text = StagePresetDetailText(presetId, row.SourceStage);
        }

        private static string StagePresetDetailText(int presetId, CustomPlanStage customStage)
        {
            if (presetId != 0)
            {
                var preset = StagePresetDefinition(presetId);
                var energyDefaults = BlackBarAllocator.DefaultPriorities((CustomStageFocus) preset.EnergyFocus, true);
                var magicDefaults = BlackBarAllocator.DefaultPriorities((CustomStageFocus) preset.MagicFocus, false);
                var energy = energyDefaults == null || energyDefaults.Length == 0 ? "none" : string.Join(", ", energyDefaults);
                var magic = magicDefaults == null || magicDefaults.Length == 0 ? "none" : string.Join(", ", magicDefaults);
                var prep = presetId == 3 ? " First half: energy AT-3, AT-4, WAN, NGU, TM; magic RIT, WAN, NGU, TM (when AT is unlocked)." : string.Empty;
                return $"{preset.Details} Default energy: {energy}. Default magic: {magic}. Outer priorities fill buffered caps in order; excess flows to later priorities. Generic NGU shares by positive plan weights with capped redistribution; explicit NGU-n targets stay ordered. Growing caps use lookahead buffers (default {BlackBarAllocator.DefaultLookaheadSeconds}s). Custom orders override these.{prep}";
            }

            customStage = customStage ?? NewDefaultCustomStage();
            var gear = customStage.GearGoal >= 0 && GearOptimizer.GoalLabels.ContainsKey(customStage.GearGoal)
                ? GearOptimizer.GoalLabels[customStage.GearGoal]
                : "Current gear";
            return $"Energy: {CustomStageAllocator.FocusLabel(customStage.EnergyFocus)}. Magic: {CustomStageAllocator.FocusLabel(customStage.MagicFocus)}. Gear: {gear}.";
        }

        private void RefreshStageRowPreviews()
        {
            if (_customStageRows == null || _customStageRows.Count == 0)
                return;

            var stages = new CustomPlanStage[_customStageRows.Count];
            for (var i = 0; i < _customStageRows.Count; i++)
            {
                var row = _customStageRows[i];
                if (!TryReadStageWeight(row.Percent.Text, out var weight))
                {
                    row.Duration.Text = "Flex weight must be a finite number greater than 0; scientific notation is allowed.";
                    return;
                }
                stages[i] = new CustomPlanStage
                {
                    CompletionMode = ReadComboValue(row.CompletionMode, (int) CustomStageCompletionMode.TimePercent),
                    DurationSeconds = decimal.ToInt32(row.FixedSeconds.Value),
                    Percent = weight
                };
            }

            var plan = new CustomPlan
            {
                TimeTriggerEnabled = _customTimeEnabled.Checked,
                TimeTriggerSeconds = decimal.ToInt32(_customHours.Value) * 3600
                                     + decimal.ToInt32(_customMinutes.Value) * 60 + decimal.ToInt32(_customSeconds.Value),
                StageDurationSeconds = decimal.ToInt32(_customStageHours.Value) * 3600
                                       + decimal.ToInt32(_customStageMinutes.Value) * 60 + decimal.ToInt32(_customStageSeconds.Value),
                Stages = stages
            };
            int[] durations;
            try
            {
                durations = CustomStageAllocator.EffectiveStageDurations(plan);
            }
            catch (InvalidOperationException error)
            {
                foreach (var row in _customStageRows)
                    row.Duration.Text = error.Message;
                return;
            }

            for (var i = 0; i < _customStageRows.Count; i++)
            {
                var row = _customStageRows[i];
                row.Container.Text = $"Step {i + 1}";
                row.DeleteButton.Enabled = _customStageRows.Count > 1;
                UpdateStageRowPresetDetails(row);
                UpdateStageRowConditionVisibility(row);

                var mode = ReadComboValue(row.CompletionMode, (int) CustomStageCompletionMode.TimePercent);
                if (mode == (int) CustomStageCompletionMode.AdvancedTrainingStats)
                {
                    row.Duration.Text = $"AT targets: mandatory {row.TargetPower.Text.Trim()}/{row.TargetToughness.Text.Trim()}; indeterminate duration";
                    continue;
                }

                row.Duration.Text = row.FixedSeconds.Value > 0
                    ? $"Fixed duration: {CustomStageAllocator.FormatSeconds(durations[i])} actual applied time required"
                    : $"Flexible share: about {CustomStageAllocator.FormatSeconds(durations[i])} nominally; absorbs delays";
            }
        }

        private void CustomStageDragHandle_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
                return;

            var control = sender as Control;
            var row = control?.Tag as CustomStageEditorRow;
            if (row != null)
                DoDragDrop(row, DragDropEffects.Move);
        }

        private void EnableCustomStageDrop(Control control)
        {
            if (control == null)
                return;

            control.AllowDrop = true;
            control.DragEnter += CustomStageRows_DragEnter;
            control.DragDrop += CustomStageRows_DragDrop;
            foreach (Control child in control.Controls)
            {
                EnableCustomStageDrop(child);
            }
        }

        private void CustomStageRows_DragEnter(object sender, DragEventArgs e)
        {
            e.Effect = e.Data.GetDataPresent(typeof(CustomStageEditorRow)) ? DragDropEffects.Move : DragDropEffects.None;
        }

        private void CustomStageRows_DragDrop(object sender, DragEventArgs e)
        {
            var row = e.Data.GetData(typeof(CustomStageEditorRow)) as CustomStageEditorRow;
            if (row == null || _customStageSteps == null)
                return;

            var sourceIndex = _customStageRows.IndexOf(row);
            if (sourceIndex < 0)
                return;

            var point = _customStageSteps.PointToClient(new Point(e.X, e.Y));
            var targetIndex = _customStageRows.Count;
            for (var i = 0; i < _customStageRows.Count; i++)
            {
                var target = _customStageRows[i].Container;
                if (point.Y < target.Top + target.Height / 2)
                {
                    targetIndex = i;
                    break;
                }
            }

            if (targetIndex > sourceIndex)
                targetIndex--;

            ReorderCustomStageRow(sourceIndex, targetIndex);
        }

        private void UpdateStageRowConditionVisibility(CustomStageEditorRow row)
        {
            var mode = ReadComboValue(row.CompletionMode, (int) CustomStageCompletionMode.TimePercent);
            var advancedTraining = mode == (int) CustomStageCompletionMode.AdvancedTrainingStats;
            row.FixedSecondsLabel.Visible = !advancedTraining;
            row.FixedSeconds.Visible = !advancedTraining;
            row.PercentLabel.Visible = !advancedTraining && row.FixedSeconds.Value == 0;
            row.Percent.Visible = !advancedTraining && row.FixedSeconds.Value == 0;
            row.TargetPowerLabel.Visible = advancedTraining;
            row.TargetPower.Visible = advancedTraining;
            row.TargetToughnessLabel.Visible = advancedTraining;
            row.TargetToughness.Visible = advancedTraining;

            if (advancedTraining)
            {
                if (string.IsNullOrWhiteSpace(row.TargetPower.Text))
                    row.TargetPower.Text = "6000";
                if (string.IsNullOrWhiteSpace(row.TargetToughness.Text))
                    row.TargetToughness.Text = "6000";
            }
        }

        private void CustomStageRow_Changed(object sender, EventArgs e)
        {
            if (_customStageRowsInitializing || _customPlanInitializing)
                return;

            RefreshStageRowPreviews();
            CustomPlanControl_Changed(sender, e);
        }

        private void CustomStageAdd_Click(object sender, EventArgs e)
        {
            InsertCustomStage(_customStageRows.Count);
        }

        private void InsertCustomStage(int index)
        {
            if (!TryReadStageRows(out var stages, out var error))
            {
                SetCustomStatus(error, true);
                return;
            }

            var list = stages.ToList();
            list.Insert(Math.Max(0, Math.Min(index, list.Count)), NewDefaultCustomStage());
            ReplaceCustomStageRowsAndSave(list.ToArray());
        }

        private void ReorderCustomStageRow(int sourceIndex, int targetIndex)
        {
            if (!TryReadStageRows(out var stages, out var error))
            {
                SetCustomStatus(error, true);
                return;
            }

            if (sourceIndex < 0 || sourceIndex >= stages.Length)
                return;

            targetIndex = Math.Max(0, Math.Min(targetIndex, stages.Length - 1));
            if (sourceIndex == targetIndex)
                return;

            var list = stages.ToList();
            var item = list[sourceIndex];
            list.RemoveAt(sourceIndex);
            list.Insert(targetIndex, item);
            ReplaceCustomStageRowsAndSave(list.ToArray());
        }

        private void DeleteCustomStageRow(CustomStageEditorRow row)
        {
            if (_customStageRows.Count <= 1)
            {
                SetCustomStatus("At least one step is required.", true);
                return;
            }

            var index = _customStageRows.IndexOf(row);
            if (index < 0)
                return;

            if (!TryReadStageRows(out var stages, out var error))
            {
                SetCustomStatus(error, true);
                return;
            }

            var list = stages.ToList();
            list.RemoveAt(index);
            ReplaceCustomStageRowsAndSave(list.ToArray());
        }

        private void ReplaceCustomStageRowsAndSave(CustomPlanStage[] stages)
        {
            LoadCustomStageRows(stages);
            SaveCustomPlanFromUi();
        }

        private static CustomPlanStage NewDefaultCustomStage()
        {
            return new CustomPlanStage
            {
                Name = "NGU sustain",
                Percent = 100.0,
                EnergyFocus = (int) CustomStageFocus.EnergyNgu,
                MagicFocus = (int) CustomStageFocus.MagicNgu,
                GearGoal = (int) GearOptimizationGoal.EnergyNgu,
                CompletionMode = (int) CustomStageCompletionMode.TimePercent
            };
        }

        private static CustomPlanStage StageFromRowPreset(CustomStageEditorRow row, int index)
        {
            var presetId = ReadComboValue(row.Preset, 0);
            var preset = StagePresetDefinition(presetId);
            var stage = CloneStage(row.SourceStage);
            if (presetId != 0)
            {
                stage.EnergyFocus = preset.EnergyFocus;
                stage.MagicFocus = preset.MagicFocus;
                stage.GearGoal = preset.GearGoal;
            }

            stage.Name = string.IsNullOrWhiteSpace(row.Name.Text)
                ? preset.Name == "Custom" ? $"Step {index + 1}" : preset.Name
                : row.Name.Text.Trim();
            return stage;
        }

        private bool TryReadStageRows(out CustomPlanStage[] stages, out string error)
        {
            var parsed = new List<CustomPlanStage>();
            if (_customStageRows.Count == 0)
            {
                stages = null;
                error = "At least one custom step is required.";
                return false;
            }

            for (var i = 0; i < _customStageRows.Count; i++)
            {
                var row = _customStageRows[i];
                var mode = ReadComboValue(row.CompletionMode, (int) CustomStageCompletionMode.TimePercent);
                var stage = StageFromRowPreset(row, i);
                stage.CompletionMode = mode;
                if (!TryReadStageWeight(row.Percent.Text, out stage.Percent))
                {
                    stages = null;
                    error = $"Step {i + 1} flex weight must be a finite number greater than 0; scientific notation is allowed.";
                    return false;
                }
                stage.DurationSeconds = decimal.ToInt32(row.FixedSeconds.Value);
                stage.BlackBarLookaheadSeconds = decimal.ToInt32(row.BlackBarLookaheadSeconds.Value);
                if (row.FightScheduleEnabled.Checked)
                {
                    stage.FightSchedule = stage.FightSchedule ?? new CustomFightSchedule();
                    stage.FightSchedule.Anchor = ReadComboValue(row.FightAnchor, 0);
                    stage.FightSchedule.DelaySeconds = decimal.ToInt32(row.FightDelaySeconds.Value);
                    stage.FightSchedule.IntervalSeconds = decimal.ToInt32(row.FightIntervalSeconds.Value);
                }
                else stage.FightSchedule = null;

                if (!TryReadStagePriorities(row.EnergyPriorities.Text, true, out stage.EnergyPriorities, out var priorityError)
                    || !TryReadStagePriorities(row.MagicPriorities.Text, false, out stage.MagicPriorities, out priorityError))
                {
                    stages = null;
                    error = $"Step {i + 1}: {priorityError}";
                    return false;
                }

                if (mode == (int) CustomStageCompletionMode.AdvancedTrainingStats)
                {
                    if (!TryParseCompactLong(row.TargetPower.Text, out stage.TargetAdvancedTrainingPower) || stage.TargetAdvancedTrainingPower <= 0)
                    {
                        stages = null;
                        error = $"Step {i + 1} Advanced Training power target must be a positive number, like 6000 or 6k.";
                        return false;
                    }

                    if (!TryParseCompactLong(row.TargetToughness.Text, out stage.TargetAdvancedTrainingToughness) || stage.TargetAdvancedTrainingToughness <= 0)
                    {
                        stages = null;
                        error = $"Step {i + 1} Advanced Training toughness target must be a positive number, like 6000 or 6k.";
                        return false;
                    }
                }

                row.SourceStage = CloneStage(stage);
                parsed.Add(stage);
            }

            stages = parsed.ToArray();
            error = null;
            return true;
        }

        private static bool TryReadStageWeight(string text, out double weight)
        {
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out weight)
                   && weight > 0d && !double.IsNaN(weight) && !double.IsInfinity(weight);
        }

        private static bool TryReadStagePriorities(string text, bool energy, out string[] priorities, out string error)
        {
            priorities = string.IsNullOrWhiteSpace(text)
                ? new string[0]
                : text.Split(',').Select(x => x.Trim().ToUpperInvariant()).ToArray();
            var invalid = priorities.FirstOrDefault(x => !BlackBarAllocator.IsValidPriority(x, energy));
            if (invalid != null)
            {
                error = $"Invalid {(energy ? "energy" : "magic")} priority '{invalid}'. Use comma-separated supported tokens, or leave blank for preset defaults.";
                return false;
            }

            error = null;
            return true;
        }

        private static int ReadComboValue(ComboBox combo, int fallback)
        {
            if (combo == null)
                return fallback;

            if (combo.SelectedValue is int selectedValue)
                return selectedValue;

            if (combo.SelectedItem is KeyValuePair<int, string> selectedItem)
                return selectedItem.Key;

            return fallback;
        }

        private static bool TryParseCompactLong(string text, out long value)
        {
            value = 0;
            var raw = (text ?? string.Empty).Trim().Replace(",", string.Empty);
            if (raw.Length == 0)
                return false;

            var multiplier = 1d;
            var suffix = char.ToLowerInvariant(raw[raw.Length - 1]);
            if (suffix == 'k' || suffix == 'm' || suffix == 'b' || suffix == 't')
            {
                raw = raw.Substring(0, raw.Length - 1).Trim();
                switch (suffix)
                {
                    case 'k':
                        multiplier = 1000d;
                        break;
                    case 'm':
                        multiplier = 1000000d;
                        break;
                    case 'b':
                        multiplier = 1000000000d;
                        break;
                    case 't':
                        multiplier = 1000000000000d;
                        break;
                }
            }

            double parsed;
            if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed) || parsed < 0d)
                return false;

            var scaled = parsed * multiplier;
            if (double.IsNaN(scaled) || double.IsInfinity(scaled) || scaled > long.MaxValue)
                return false;

            value = (long) Math.Round(scaled);
            return true;
        }

        private static string StagesToText(CustomPlanStage[] stages)
        {
            if (stages == null || stages.Length == 0)
                return string.Empty;

            return string.Join(Environment.NewLine, stages.Select(stage =>
                string.Format(CultureInfo.InvariantCulture,
                    "{0} | {1:0.###} | {2} | {3} | {4}",
                    stage.Name,
                    stage.Percent,
                    CustomStageAllocator.FocusLabel(stage.EnergyFocus),
                    CustomStageAllocator.FocusLabel(stage.MagicFocus),
                    GearGoalName(stage.GearGoal))).ToArray());
        }

        private static bool TryParseStages(string text, out CustomPlanStage[] stages, out string error)
        {
            var parsed = new List<CustomPlanStage>();
            var lines = (text ?? string.Empty)
                .Split(new[] {"\r\n", "\n"}, StringSplitOptions.None)
                .Select(x => x.Trim())
                .Where(x => x.Length > 0)
                .ToArray();

            for (var i = 0; i < lines.Length; i++)
            {
                var parts = lines[i].Split('|').Select(x => x.Trim()).ToArray();
                if (parts.Length != 5)
                {
                    stages = null;
                    error = $"Stage line {i + 1} must have 5 pipe-separated fields.";
                    return false;
                }

                if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var percent) || percent <= 0)
                {
                    stages = null;
                    error = $"Stage line {i + 1} percent must be greater than 0.";
                    return false;
                }

                if (!TryParseFocus(parts[2], out var energyFocus))
                {
                    stages = null;
                    error = $"Stage line {i + 1} has unknown energy focus '{parts[2]}'.";
                    return false;
                }

                if (!TryParseFocus(parts[3], out var magicFocus))
                {
                    stages = null;
                    error = $"Stage line {i + 1} has unknown magic focus '{parts[3]}'.";
                    return false;
                }

                if (!TryParseGearGoal(parts[4], out var gearGoal))
                {
                    stages = null;
                    error = $"Stage line {i + 1} has unknown gear goal '{parts[4]}'.";
                    return false;
                }

                parsed.Add(new CustomPlanStage
                {
                    Name = string.IsNullOrWhiteSpace(parts[0]) ? $"Stage {i + 1}" : parts[0],
                    Percent = percent,
                    EnergyFocus = energyFocus,
                    MagicFocus = magicFocus,
                    GearGoal = gearGoal
                });
            }

            if (parsed.Count == 0)
            {
                stages = null;
                error = "At least one custom stage is required.";
                return false;
            }

            stages = parsed.ToArray();
            error = null;
            return true;
        }

        private static bool TryParseFocus(string text, out int focus)
        {
            var normalized = NormalizeToken(text);
            if (normalized == "none" || normalized == "off" || normalized == "")
            {
                focus = (int) CustomStageFocus.None;
                return true;
            }

            if (normalized == "tm" || normalized == "timemachine")
            {
                focus = (int) CustomStageFocus.TimeMachine;
                return true;
            }

            if (normalized == "aug" || normalized == "augs" || normalized == "augment" || normalized == "augments")
            {
                focus = (int) CustomStageFocus.Augments;
                return true;
            }

            if (normalized == "wan" || normalized == "wandoos")
            {
                focus = (int) CustomStageFocus.Wandoos;
                return true;
            }

            if (normalized == "blood" || normalized == "bloodmagic" || normalized == "ritual" || normalized == "rituals")
            {
                focus = (int) CustomStageFocus.BloodMagic;
                return true;
            }

            if (normalized == "energyngu" || normalized == "engu")
            {
                focus = (int) CustomStageFocus.EnergyNgu;
                return true;
            }

            if (normalized == "magicngu" || normalized == "mngu")
            {
                focus = (int) CustomStageFocus.MagicNgu;
                return true;
            }

            if (normalized == "advancedtraining" || normalized == "advtraining" || normalized == "at")
            {
                focus = (int) CustomStageFocus.AdvancedTraining;
                return true;
            }

            focus = (int) CustomStageFocus.None;
            return false;
        }

        private static bool TryParseGearGoal(string text, out int goal)
        {
            var normalized = NormalizeToken(text);
            if (normalized == "none" || normalized == "off" || normalized == "" || normalized == "-1")
            {
                goal = -1;
                return true;
            }

            if (int.TryParse(text, out goal))
            {
                return goal >= -1 && (goal < 0 || GearOptimizer.GoalLabels.ContainsKey(goal));
            }

            foreach (var value in GearOptimizer.GoalLabels)
            {
                if (NormalizeToken(value.Value) == normalized)
                {
                    goal = value.Key;
                    return true;
                }
            }

            foreach (var name in Enum.GetNames(typeof(GearOptimizationGoal)))
            {
                if (NormalizeToken(name) == normalized)
                {
                    goal = (int) Enum.Parse(typeof(GearOptimizationGoal), name);
                    return true;
                }
            }

            goal = -1;
            return false;
        }

        private static string GearGoalName(int gearGoal)
        {
            if (gearGoal < 0)
                return "None";

            var name = Enum.GetName(typeof(GearOptimizationGoal), gearGoal);
            return string.IsNullOrEmpty(name) ? "None" : name;
        }

        private static string NormalizeToken(string text)
        {
            return new string((text ?? string.Empty)
                    .Where(char.IsLetterOrDigit)
                    .Select(char.ToLowerInvariant)
                    .ToArray());
        }

        internal void RefreshCustomHistory(CustomPlan plan)
        {
            if (_customRebirthHistory == null)
                return;

            if (plan == null || plan.RebirthHistory == null || plan.RebirthHistory.Length == 0)
            {
                _customRebirthHistory.Text = "No custom rebirths recorded yet.";
                return;
            }

            _customRebirthHistory.Text = string.Join(Environment.NewLine, plan.RebirthHistory.Select(x =>
                $"{x.TimestampLocal} | t {CustomStageAllocator.FormatSeconds((int) x.RebirthSeconds),8} | NUMBER {FormatCompact(x.NumberBefore)} -> {FormatCompact(x.NumberAfter)} | x{FormatCompact(x.NumberMultiplier)} | boss {x.BossId} | {x.Reason}").ToArray());
        }

        private void RefreshCustomIronPillHistory(SavedSettings settings)
        {
            if (_customIronPillHistory == null)
                return;

            var history = settings?.CustomIronPillHistory;
            var text = history == null || history.Length == 0
                ? "No successful automatic Iron Pill casts recorded yet."
                : string.Join(Environment.NewLine, history.Where(x => x != null)
                    .OrderByDescending(x => DateTimeOffset.TryParse(x.CastAtUtc, CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal, out var time) ? time : DateTimeOffset.MinValue).Take(10).Select(x =>
                    {
                        var timestamp = DateTimeOffset.TryParse(x.CastAtUtc, CultureInfo.InvariantCulture,
                            DateTimeStyles.AssumeUniversal, out var castAt)
                            ? castAt.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture)
                            : x.CastAtUtc;
                        var wait = CustomStageAllocator.FormatSeconds((int) Math.Min(int.MaxValue, Math.Max(0d, x.WaitSeconds)));
                        var rebirth = CustomStageAllocator.FormatSeconds((int) Math.Min(int.MaxValue, Math.Max(0d, x.RebirthSeconds)));
                        return $"{timestamp} | Wait {wait} | Pill worth {FormatCompact(x.Worth)} | Rebirth {rebirth}";
                    }).ToArray());
            if (_customIronPillHistory.Text != text)
                _customIronPillHistory.Text = text;
        }

        internal void UpdateCustomRuntimeStatus()
        {
            if (_customRuntimeStatus == null)
                return;

            if ((DateTime.Now - _lastCustomRuntimeStatusUpdate).TotalMilliseconds < 500)
                return;

            _lastCustomRuntimeStatusUpdate = DateTime.Now;
            var status = Main.GetCustomAutomationStatus();
            if (_customRuntimeStatus.Text != status)
            {
                _customRuntimeStatus.Text = status;
            }

            ApplyRuntimeStatusStyle(_customRuntimeStatus, status);

            if (_manualAdventureStatus != null && _manualAdventureStatus.Text != status)
            {
                _manualAdventureStatus.Text = status;
            }

            ApplyRuntimeStatusStyle(_manualAdventureStatus, status);

            UpdateCustomFocusButtonHighlights();
            UpdateCustomBloodGoalEstimates();
            RefreshCustomIronPillHistory(Main.Settings);
            UpdateCustomRebirthDashboard(_customStageSummaryPlan);
        }

        private static void ApplyRuntimeStatusStyle(Label label, string status)
        {
            if (label == null)
                return;

            var warning = !string.IsNullOrWhiteSpace(status) && status.StartsWith("WARNING:", StringComparison.OrdinalIgnoreCase);
            label.ForeColor = warning ? Color.DarkRed : Color.DarkSlateGray;
            label.BackColor = warning ? Color.MistyRose : SystemColors.Control;
        }

        private void UpdateCustomFocusButtonHighlights()
        {
            if (_customFocusButtons.Count == 0)
                return;

            var active = Main.reference?.GetActiveCustomFocusGoal();
            foreach (var pair in _customFocusButtons)
            {
                var isActive = active.HasValue && active.Value == pair.Key;
                if (isActive)
                {
                    pair.Value.UseVisualStyleBackColor = false;
                    pair.Value.BackColor = Color.RoyalBlue;
                    pair.Value.ForeColor = Color.White;
                    continue;
                }

                pair.Value.BackColor = SystemColors.Control;
                pair.Value.ForeColor = SystemColors.ControlText;
                pair.Value.UseVisualStyleBackColor = true;
            }
        }

        private static string FormatCompact(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                return value.ToString(CultureInfo.InvariantCulture);

            var abs = Math.Abs(value);
            if (abs >= 100000d || abs > 0d && abs < 0.001d)
                return value.ToString("0.###E+0", CultureInfo.InvariantCulture);

            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private void ReloadGoalPresetUi(string planName)
        {
            var plan = Main.CustomPlanStore.Load(planName);
            _customPlanInitializing = true;
            try
            {
                LoadGoalPresetsIntoUi(plan);
            }
            finally
            {
                _customPlanInitializing = false;
            }

            SetCustomStatus($"Loaded goal preset '{plan.GoalPresets[plan.ActiveGoalPresetIndex].Name}'");
        }

        private void CustomRunFightPrep_Click(object sender, EventArgs e)
        {
            if (Main.reference == null)
            {
                SetCustomStatus("Injector instance is not ready.", true);
                return;
            }

            SetCustomStatus(Main.reference.RunManualPreparationIngress(false));
            UpdateCustomFocusButtonHighlights();
        }

        private void CustomRunRebirth_Click(object sender, EventArgs e)
        {
            if (Main.reference == null)
            {
                SetCustomStatus("Injector instance is not ready.", true);
                return;
            }

            SetCustomStatus(Main.reference.RunManualPreparationIngress(true));
            UpdateCustomFocusButtonHighlights();
        }

        private void CustomRunSafeProbe_Click(object sender, EventArgs e)
        {
            if (Main.reference == null)
            {
                SetCustomStatus("Injector instance is not ready.", true);
                return;
            }

            SetCustomStatus(Main.reference.RunSafeCustomProbe());
        }

        private void CustomRunProbeCommands_Click(object sender, EventArgs e)
        {
            if (Main.reference == null)
            {
                SetCustomStatus("Injector instance is not ready.", true);
                return;
            }

            SetCustomStatus(Main.reference.RunProbeCommandFile());
        }

        private static GroupBox NewGroup(string title)
        {
            return new GroupBox
            {
                Text = title,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Top,
                Padding = new Padding(12, 10, 12, 12),
                Margin = new Padding(0, 0, 0, 12),
                MinimumSize = new Size(760, 0)
            };
        }

        private static GroupBox NewInnerGroup(string title)
        {
            return new GroupBox
            {
                Text = title,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Top,
                Padding = new Padding(10, 8, 10, 10),
                Margin = new Padding(0, 0, 0, 10),
                MinimumSize = new Size(720, 0)
            };
        }

        private static TableLayoutPanel NewTwoColumnLayout()
        {
            var layout = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Top,
                ColumnCount = 2,
                Padding = new Padding(2, 4, 2, 2),
                Margin = new Padding(0)
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, CustomLabelColumnWidth));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            return layout;
        }

        private static TableLayoutPanel NewSingleColumnLayout()
        {
            var layout = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Top,
                ColumnCount = 1,
                Padding = new Padding(2, 4, 2, 2),
                Margin = new Padding(0)
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            return layout;
        }

        private static Label NewLabel(string text)
        {
            return new Label
            {
                Text = text,
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Padding = new Padding(0, 7, 10, 0),
                Margin = new Padding(0, 2, 8, 4),
                MaximumSize = new Size(CustomLabelColumnWidth - 12, 0)
            };
        }

        private static Label NewInlineLabel(string text)
        {
            return new Label {Text = text, AutoSize = true, Padding = new Padding(4, 7, 8, 0), Margin = new Padding(0, 3, 0, 3)};
        }

        private static Button NewButton(string text, EventHandler handler)
        {
            var button = new Button
            {
                Text = text,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                MinimumSize = new Size(96, 31),
                Padding = new Padding(10, 3, 10, 3),
                Margin = new Padding(4, 4, 8, 4)
            };
            button.Click += (sender, args) =>
            {
                var owner = button.FindForm() as SettingsForm;
                if (owner == null)
                {
                    handler(sender, args);
                    return;
                }

                owner.RunCustomUiAction(text, () => handler(sender, args));
            };
            return button;
        }

        private static Button NewSmallButton(string text, EventHandler handler)
        {
            var button = NewButton(text, handler);
            button.MinimumSize = new Size(64, 31);
            button.Padding = new Padding(8, 3, 8, 3);
            button.Margin = new Padding(3, 4, 4, 4);
            return button;
        }

        private Button NewFocusButton(string text, CustomFocusGoal goal)
        {
            var button = NewButton(text, (sender, args) => CustomFocusGoal_Click(goal));
            _customFocusButtons[goal] = button;
            return button;
        }

        private NumericUpDown NewNumber(int min, int max)
        {
            var number = new NumericUpDown {Minimum = min, Maximum = max, Width = 76, Margin = new Padding(3, 4, 6, 4)};
            number.ValueChanged += CustomPlanControl_Changed;
            return number;
        }

        private static NumericUpDown NewSettingsNumber(int min, int max)
        {
            return new NumericUpDown {Minimum = min, Maximum = max, Width = 76, Margin = new Padding(3, 4, 6, 4)};
        }

        private static TextBox NewTextBox(int width)
        {
            return new TextBox
            {
                Width = width,
                MinimumSize = new Size(Math.Min(width, CustomEditorMinWidth), 0),
                Margin = new Padding(3, 4, 10, 4)
            };
        }

        private static ComboBox NewComboBox(int width)
        {
            return new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = width,
                MinimumSize = new Size(Math.Min(width, CustomEditorMinWidth), 0),
                Margin = new Padding(3, 4, 10, 4)
            };
        }

        private CheckBox NewPlanCheckBox(string text)
        {
            var checkBox = NewCheckBox(text);
            checkBox.CheckedChanged += CustomPlanControl_Changed;
            return checkBox;
        }

        private static CheckBox NewCheckBox(string text)
        {
            return new CheckBox
            {
                Text = text,
                AutoSize = true,
                Margin = new Padding(3, 5, 12, 5),
                MaximumSize = new Size(680, 0)
            };
        }

        private static FlowLayoutPanel NewInlineFlowPanel()
        {
            return new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                Padding = new Padding(0),
                Margin = new Padding(0, 0, 0, 4),
                MinimumSize = new Size(CustomEditorMinWidth, 0)
            };
        }

        private static FlowLayoutPanel NewWrappingFlowPanel()
        {
            return new ResponsiveFlowLayoutPanel
            {
                AutoSize = false,
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                Height = 40,
                Padding = new Padding(0),
                Margin = new Padding(0, 0, 0, 4),
                MaximumSize = new Size(720, 0),
                MinimumSize = new Size(0, 0)
            };
        }

        private sealed class ResponsiveFlowLayoutPanel : FlowLayoutPanel
        {
            private bool _updatingHeight;

            public override Size GetPreferredSize(Size proposedSize)
            {
                var width = GetAvailableWidth(proposedSize.Width);
                var preferred = base.GetPreferredSize(new Size(width, proposedSize.Height));
                preferred.Width = width;
                return preferred;
            }

            protected override void OnControlAdded(ControlEventArgs e)
            {
                base.OnControlAdded(e);
                UpdateWrappedHeight();
            }

            protected override void OnControlRemoved(ControlEventArgs e)
            {
                base.OnControlRemoved(e);
                UpdateWrappedHeight();
            }

            protected override void OnLayout(LayoutEventArgs levent)
            {
                base.OnLayout(levent);
                UpdateWrappedHeight();
            }

            protected override void OnSizeChanged(EventArgs e)
            {
                base.OnSizeChanged(e);
                UpdateWrappedHeight();
            }

            private void UpdateWrappedHeight()
            {
                if (_updatingHeight)
                    return;

                var width = GetAvailableWidth(ClientSize.Width > 0 ? ClientSize.Width : Width);
                if (width <= 0)
                    return;

                _updatingHeight = true;
                try
                {
                    var preferredHeight = base.GetPreferredSize(new Size(width, 0)).Height;
                    if (preferredHeight > 0 && Height != preferredHeight)
                        Height = preferredHeight;
                }
                finally
                {
                    _updatingHeight = false;
                }
            }

            private int GetAvailableWidth(int proposedWidth)
            {
                var width = proposedWidth;
                var table = Parent as TableLayoutPanel;
                if (table != null)
                {
                    var position = table.GetPositionFromControl(this);
                    var widths = table.GetColumnWidths();
                    if (position.Column >= 0 && position.Column < widths.Length)
                        width = widths[position.Column] - Margin.Horizontal;
                }
                else if (width <= 0 && Parent != null)
                {
                    width = Parent.ClientSize.Width - Margin.Horizontal;
                }

                if (MaximumSize.Width > 0)
                    width = width <= 0 ? MaximumSize.Width : Math.Min(width, MaximumSize.Width);

                if (width <= 0)
                    width = CustomWideEditorMinWidth;

                return Math.Max(1, width);
            }
        }

        private static FlowLayoutPanel NewStackPanel()
        {
            return new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Padding = new Padding(0),
                Margin = new Padding(0, 0, 0, 4),
                MinimumSize = new Size(CustomEditorMinWidth, 0)
            };
        }

        private void SetCustomStatus(string text)
        {
            SetCustomStatus(text, false);
        }

        private void SetCustomStatus(string text, bool error)
        {
            if (_customStatus != null)
            {
                _customStatus.Text = text;
                _customStatus.ForeColor = error ? Color.DarkRed : SystemColors.ControlDarkDark;
                _customStatus.BackColor = error ? Color.MistyRose : SystemColors.Control;
            }
        }

        internal void ReportCustomError(string text)
        {
            SetCustomStatus(text, true);
        }

        internal void ReportCustomStatus(string text)
        {
            SetCustomStatus(text);
        }

        private void RunCustomUiAction(string actionName, Action action)
        {
            RunCustomUiAction(actionName, action, true);
        }

        private void RunCustomUiAction(string actionName, Action action, bool showDialog)
        {
            if (_initializing && !showDialog)
                return;

            try
            {
                action();
            }
            catch (Exception e)
            {
                var message = Main.LogError($"Custom UI action '{actionName}'", e);
                SetCustomStatus(message, true);
                if (showDialog)
                {
                    MessageBox.Show(message + Environment.NewLine + Environment.NewLine + e, "NGUInjector Custom Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private static string PromptForPlanName(string title, string label, string defaultValue)
        {
            using (var form = new Form())
            using (var input = new TextBox())
            using (var ok = new Button())
            using (var cancel = new Button())
            using (var prompt = new Label())
            {
                form.Text = title;
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.StartPosition = FormStartPosition.CenterParent;
                form.ClientSize = new Size(320, 110);
                form.MinimizeBox = false;
                form.MaximizeBox = false;

                prompt.Text = label;
                prompt.Location = new Point(12, 12);
                prompt.AutoSize = true;
                input.Text = defaultValue;
                input.Location = new Point(12, 34);
                input.Width = 296;
                ok.Text = "OK";
                ok.DialogResult = DialogResult.OK;
                ok.Location = new Point(152, 72);
                cancel.Text = "Cancel";
                cancel.DialogResult = DialogResult.Cancel;
                cancel.Location = new Point(233, 72);
                form.Controls.Add(prompt);
                form.Controls.Add(input);
                form.Controls.Add(ok);
                form.Controls.Add(cancel);
                form.AcceptButton = ok;
                form.CancelButton = cancel;

                return form.ShowDialog() == DialogResult.OK ? input.Text : null;
            }
        }
    }
}
