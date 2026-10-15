using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Reflection;
using System.Runtime.Remoting.Messaging;
using System.Security.Policy;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using NGUInjector.AllocationProfiles;
using NGUInjector.CustomPlans;
using NGUInjector.Managers;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Application = UnityEngine.Application;

namespace NGUInjector
{
    internal partial class Main : MonoBehaviour
    {
        internal static InventoryController Controller;
        internal static Character Character;
        internal static PlayerController PlayerController;
        internal static StreamWriter OutputWriter;
        internal static StreamWriter LootWriter;
        internal static StreamWriter CombatWriter;
        internal static StreamWriter AllocationWriter;
        internal static StreamWriter PitSpinWriter;
        internal static Main reference;
        private YggdrasilManager _yggManager;
        private InventoryManager _invManager;
        private CombatManager _combManager;
        private QuestManager _questManager;
        private static CustomAllocation _profile;
        private float _timeLeft = 10.0f;
        internal static SettingsForm settingsForm;
        internal static WishManager WishManager;
        internal const string Version = "3.4.2";
        private static int _furthestZone;

        internal static bool Test { get; set; }

        private static string _dir;
        private static string _profilesDir;
        private static string _customPlansDir;

        private static bool _tempSwapped = false;

        internal static FileSystemWatcher ConfigWatcher;
        internal static FileSystemWatcher AllocationWatcher;
        internal static FileSystemWatcher ZoneWatcher;

        internal static SavedSettings Settings;
        internal static CustomPlanStore CustomPlanStore;

        public void Start()
        {
            Loader.TraceStartup("Main.Start entered.");
            try
            {
                var directory = Path.Combine(Environment.ExpandEnvironmentVariables("%userprofile%/Desktop"), "NGUInjector");
                Directory.CreateDirectory(directory);
                _injectorLease = JsonFiles.AcquireInjectorLease(directory);
                if (reference != null && !ReferenceEquals(reference, this))
                    throw new InvalidOperationException("An injector instance is already active.");
                reference = this;
                _dir = directory;
                _profilesDir = Path.Combine(_dir, "profiles");
                _customPlansDir = Path.Combine(_dir, "custom-plans");
                InitializeExternalUi();

                var logDir = Path.Combine(_dir, "logs");
                if (!Directory.Exists(logDir))
                {
                    Directory.CreateDirectory(logDir);
                }

                OutputWriter = new StreamWriter(Path.Combine(logDir, "inject.log")) {AutoFlush = true};
                LootWriter = new StreamWriter(Path.Combine(logDir, "loot.log")) {AutoFlush = true};
                CombatWriter = new StreamWriter(Path.Combine(logDir, "combat.log")) {AutoFlush = true};
                AllocationWriter = new StreamWriter(Path.Combine(logDir, "allocation.log")) {AutoFlush = true};
                PitSpinWriter = new StreamWriter(Path.Combine(logDir, "pitspin.log"), true) {AutoFlush = true};

                if (!Directory.Exists(_profilesDir))
                {
                    Directory.CreateDirectory(_profilesDir);
                }

                if (!Directory.Exists(_customPlansDir))
                {
                    Directory.CreateDirectory(_customPlansDir);
                }

                var oldPath = Path.Combine(_dir, "allocation.json");
                var newPath = Path.Combine(_profilesDir, "default.json");

                if (File.Exists(oldPath) && !File.Exists(newPath))
                {
                    File.Move(oldPath, newPath);
                }
            }
            catch (Exception e)
            {
                FailStartup(e);
                return;
            }
            
            try
            {
                Character = FindObjectOfType<Character>();

                Log("Injected");
                LogLoot("Starting Loot Writer");
                LogCombat("Starting Combat Writer");
                Controller = Character.inventoryController;
                PlayerController = FindObjectOfType<PlayerController>();
                _invManager = new InventoryManager();
                _yggManager = new YggdrasilManager();
                _questManager = new QuestManager();
                _combManager = new CombatManager();
                LoadoutManager.ReleaseLock();
                DiggerManager.ReleaseLock();

                Settings = new SavedSettings(_dir);

                if (!Settings.LoadSettings())
                {
                    if (File.Exists(Path.Combine(_dir, "settings.json")))
                        throw new InvalidOperationException("Existing settings could not be loaded; refusing to overwrite them.");
                    var temp = new SavedSettings(null)
                    {
                        PriorityBoosts = new int[] { },
                        YggdrasilLoadout = new int[] { },
                        SwapYggdrasilLoadouts = false,
                        SwapTitanLoadouts = false,
                        TitanLoadout = new int[] { },
                        ManageDiggers = true,
                        ManageYggdrasil = false,
                        ManageEnergy = true,
                        ManageMagic = true,
                        ManageInventory = true,
                        ManageGear = true,
                        AutoConvertBoosts = true,
                        SnipeZone = 0,
                        FastCombat = false,
                        PrecastBuffs = true,
                        AutoFight = false,
                        AutoQuest = false,
                        AutoQuestITOPOD = false,
                        AllowMajorQuests = false,
                        GoldDropLoadout = new int[] {},
                        AutoMoneyPit = false,
                        AutoSpin = false,
                        MoneyPitLoadout = new int[] {},
                        AutoRebirth = false,
                        ManageWandoos = false,
                        MoneyPitThreshold = 1e5,
                        DoGoldSwap = false,
                        BoostBlacklist = new int[] {},
                        CombatMode = 0,
                        RecoverHealth = false,
                        SnipeBossOnly = true,
                        AllowZoneFallback = false,
                        QuestFastCombat = true,
                        AbandonMinors = false,
                        MinorAbandonThreshold = 30,
                        QuestCombatMode = 0,
                        AutoBuyEM = false,
                        AutoSpellSwap = false,
                        CounterfeitThreshold = 400,
                        SpaghettiThreshold = 30,
                        BloodNumberThreshold = 1e10,
                        CastBloodSpells = false,
                        IronPillThreshold = 10000,
                        BloodMacGuffinAThreshold = 6,
                        BloodMacGuffinBThreshold = 6,
                        CubePriority = 0,
                        CombatEnabled = false,
                        GlobalEnabled = false,
                        QuickDiggers = new int[] {},
                        QuickLoadout = new int[] {},
                        UseButterMajor = false,
                        ManualMinors =  false,
                        UseButterMinor = false,
                        ActivateFruits = false,
                        ManageR3 = true,
                        WishPriorities = new int[] {},
                        BeastMode = true,
                        ManageNGUDiff = true,
                        AllocationFile = "default",
                        TitanGoldTargets = new bool[ZoneHelpers.TitanZones.Length],
                        ManageGoldLoadouts = false,
                        ResnipeTime = 3600,
                        TitanMoneyDone = new bool[ZoneHelpers.TitanZones.Length],
                        TitanSwapTargets = new bool[ZoneHelpers.TitanZones.Length],
                        GoldCBlockMode = false,
                        DebugAllocation = false,
                        AdventureTargetITOPOD = false,
                        ITOPODRecoverHP = false,
                        ITOPODCombatMode = 0,
                        ITOPODBeastMode = true,
                        ITOPODFastCombat = true,
                        ITOPODPrecastBuffs = false,
                        DisableOverlay = false,
                        OptimizeITOPODFloor = false,
                        YggSwapThreshold = 1,
                        UpgradeDiggers = true,
                        BlacklistedBosses = new int[0],
                        SpecialBoostBlacklist = new int[0],
                        MoreBlockParry = false,
                        WishSortOrder = false,
                        WishSortPriorities = false,
                        HackAdvance = false,
                        UseCustomRebirthPlan = false,
                        CustomPlanFile = "default",
                        CustomAdventureEnabled = false,
                        CustomAdventureMode = 0,
                        CustomAdventureZone = 0,
                        CustomAdventureGoldPush = true,
                        CustomAutoMoneyPitEnabled = false,
                        CustomAutoBoostEnabled = true,
                        CustomAutoBoostCubePowerRatio = 2,
                        CustomAugmentsBloodEstimateSeconds = 300,
                        CustomAutoBloodSpellsEnabled = true,
                        CustomBloodSpaghettiGoalPercent = 30,
                        CustomBloodCounterfeitGoalPercent = 400,
                        CustomIronPillSaveSeconds = 900,
                        CustomIronPillMinWorth = 0
                    };

                    Settings.MassUpdate(temp);

                    Log($"Created default settings");
                }

                CustomPlanStore = new CustomPlanStore(_customPlansDir);
                var customPlanFile = Settings.CustomPlanFile;
                if (string.IsNullOrEmpty(customPlanFile) || !CustomPlanStore.PlanExists(customPlanFile))
                {
                    Settings.SetSaveDisabled(true);
                    Settings.CustomPlanFile = CustomPlanStore.EnsureDefaultPlan();
                    Settings.SetSaveDisabled(false);
                }

                if (!ExternalUiEnabled)
                {
                    settingsForm = new SettingsForm();
                }
                else
                {
                    Log("External UI mode enabled; WinForms settings window will not be shown.");
                }

                if (string.IsNullOrEmpty(Settings.AllocationFile))
                {
                    Settings.SetSaveDisabled(true);
                    Settings.AllocationFile = "default";
                    Settings.SetSaveDisabled(false);
                }

                if (Settings.TitanGoldTargets == null || Settings.TitanGoldTargets.Length == 0)
                {
                    Settings.SetSaveDisabled(true);
                    Settings.TitanGoldTargets = new bool[ZoneHelpers.TitanZones.Length];
                    Settings.SetSaveDisabled(false);
                }

                if (Settings.TitanMoneyDone == null || Settings.TitanMoneyDone.Length == 0)
                {
                    Settings.SetSaveDisabled(true);
                    Settings.TitanMoneyDone = new bool[ZoneHelpers.TitanZones.Length];
                    Settings.SetSaveDisabled(false);
                }

                if (Settings.TitanSwapTargets == null || Settings.TitanSwapTargets.Length == 0)
                {
                    Settings.SetSaveDisabled(true);
                    Settings.TitanSwapTargets = new bool[ZoneHelpers.TitanZones.Length];
                    Settings.SetSaveDisabled(false);
                }

                if (Settings.SpecialBoostBlacklist == null)
                {
                    Settings.SetSaveDisabled(true);
                    Settings.SpecialBoostBlacklist = new int[0];
                    Settings.SetSaveDisabled(false);
                }

                if (Settings.BlacklistedBosses == null)
                {
                    Settings.SetSaveDisabled(true);
                    Settings.BlacklistedBosses = new int[0];
                    Settings.SetSaveDisabled(false);
                }

                WishManager = new WishManager();

                LoadAllocation();
                LoadAllocationProfiles();

                ZoneWatcher = new FileSystemWatcher
                {
                    Path = _dir,
                    Filter = "zoneOverride.json",
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName
                };

                ZoneWatcher.Changed += (sender, args) => QueueWatcherReload(3);
                ZoneWatcher.Created += (sender, args) => QueueWatcherReload(3);
                ZoneWatcher.Deleted += (sender, args) => QueueWatcherReload(3);
                ZoneWatcher.Renamed += (sender, args) => QueueWatcherReload(3);
                ZoneWatcher.EnableRaisingEvents = true;

                ConfigWatcher = new FileSystemWatcher
                {
                    Path = _dir,
                    Filter = "settings.json",
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName
                };

                ConfigWatcher.Changed += (sender, args) => QueueWatcherReload(0);
                ConfigWatcher.Created += (sender, args) => QueueWatcherReload(0);
                ConfigWatcher.Deleted += (sender, args) => QueueWatcherReload(0);
                ConfigWatcher.Renamed += (sender, args) => QueueWatcherReload(0);
                ConfigWatcher.EnableRaisingEvents = true;

                AllocationWatcher = new FileSystemWatcher
                {
                    Path = _profilesDir,
                    Filter = "*.json",
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName
                };

                AllocationWatcher.Changed += (sender, args) => QueueWatcherReload(1);
                AllocationWatcher.Created += (sender, args) => { QueueWatcherReload(1); QueueWatcherReload(2); };
                AllocationWatcher.Deleted += (sender, args) => { QueueWatcherReload(1); QueueWatcherReload(2); };
                AllocationWatcher.Renamed += (sender, args) => { QueueWatcherReload(1); QueueWatcherReload(2); };
                AllocationWatcher.EnableRaisingEvents = true;

                Settings.SaveSettings();
                Settings.LoadSettings();

                LogAllocation("Started Allocation Writer");

                ZoneStatHelper.CreateOverrides(_dir);

                settingsForm?.UpdateFromSettings(Settings);
                settingsForm?.Show();

                InvokeRepeating("AutomationRoutine", 0.0f, 10.0f);
                InvokeRepeating("CustomAdventureTick", 0.0f, .1f);
                if (LegacyAutomationEnabled)
                    InvokeRepeating("SnipeZone", 0.0f, .1f);
                InvokeRepeating("MonitorLog", 0.0f, 1f);
                InvokeRepeating("QuickStuff", 0.0f, .5f);
                InvokeRepeating("BossPushTick", 0.0f, .1f);
                InvokeRepeating("ShowBoostProgress", 0.0f, 60.0f);
                InvokeRepeating("SetResnipe", 0f,1f);


                RunStartupAutomationRequests();
                _started = true;
                if (ExternalUiEnabled)
                    WriteExternalUiRuntimeSnapshot(0);
            }
            catch (Exception e)
            {
                FailStartup(e);
            }
        }

        public void Update()
        {
            if (_unloaded || !_started)
                return;
            try
            {
                PumpWatcherReloads();
                var progressPercent = (int)Math.Floor(_timeLeft / 10 * 100);
                PumpExternalUi(progressPercent);
                if (_unloaded || Settings == null || _combManager == null)
                    return;

                _timeLeft -= Time.deltaTime;
                _combManager.UpdateFightTimer(Time.deltaTime);
                RunCustomSustainedFocusAllocation();

                settingsForm?.UpdateProgressBar(progressPercent);

                if (settingsForm != null && Input.GetKeyDown(KeyCode.F1))
                {
                    if (!settingsForm.Visible)
                    {
                        settingsForm.Show();
                    }

                    settingsForm.BringToFront();
                }

                if (Input.GetKeyDown(KeyCode.F2))
                {
                    Settings.GlobalEnabled = false;
                    SetCustomAutomationStatus("Legacy automation is disabled; use Custom controls instead.");
                }

                if (Input.GetKeyDown(KeyCode.F3))
                {
                    QuickSave();
                }

                if (Input.GetKeyDown(KeyCode.F7))
                {
                    QuickLoad();
                }

                if (Input.GetKeyDown(KeyCode.F4))
                {
                    Settings.AutoQuestITOPOD = !Settings.AutoQuestITOPOD;
                }

                if (Input.GetKeyDown(KeyCode.F5))
                {
                    DumpEquipped();
                }

                if (Input.GetKeyDown(KeyCode.F8))
                {
                    if (Settings.QuickLoadout != null && Settings.QuickLoadout.Length > 0)
                    {
                        if (_tempSwapped)
                        {
                            Log("Restoring Previous Loadout");
                            LoadoutManager.RestoreTempLoadout();
                        }
                        else
                        {
                            Log("Equipping Quick Loadout");
                            LoadoutManager.SaveTempLoadout();
                            LoadoutManager.ChangeGear(Settings.QuickLoadout);
                        }
                    }

                    if (Settings.QuickDiggers != null && Settings.QuickDiggers.Length > 0)
                    {
                        if (_tempSwapped)
                        {
                            Log("Equipping Previous Diggers");
                            DiggerManager.RestoreTempDiggers();
                        }
                        else
                        {
                            Log("Equipping Quick Diggers");
                            DiggerManager.SaveTempDiggers();
                            DiggerManager.EquipDiggers(Settings.QuickDiggers);
                        }
                    }

                    _tempSwapped = !_tempSwapped;
                }

                // F11 reserved for testing
                //if (Input.GetKeyDown(KeyCode.F11))
                //{
                //    Character.realExp += 10000;
                //}
            }
            catch (Exception e)
            {
                LogError("Update", e);
            }
        }

        private void QuickSave()
        {
            Log("Writing quicksave and json");
            var data = Character.importExport.getBase64Data();
            using (var writer = new StreamWriter(Path.Combine(_dir, "NGUSave.txt")))
            {
                writer.WriteLine(data);
            }

            data = JsonUtility.ToJson(Character.importExport.gameStateToData());
            using (var writer = new StreamWriter(Path.Combine(_dir, "NGUSave.json")))
            {
                writer.WriteLine(data);
            }

            Character.saveLoad.saveGamestateToSteamCloud();
        }

        private void QuickLoad()
        {
            var filename = Path.Combine(_dir, "NGUSave.txt");
            if (!File.Exists(filename))
            {
                Log("Quicksave doesn't exist");
                return;
            }

            var saveTime = File.GetLastWriteTime(filename);
            var s = DateTime.Now.Subtract(saveTime);
            var secDiff = (int)s.TotalSeconds;
            if (secDiff > 120)
            {
                var diff = saveTime.GetPrettyDate();

                var confirmResult = MessageBox.Show($"Last quicksave was {diff}. Are you sure you want to load?",
                    "Load Quicksave"
                    , MessageBoxButtons.YesNo);

                if (confirmResult == DialogResult.No)
                    return;
            }

            Log("Loading quicksave");
            string base64Data;
            try
            {
                base64Data = File.ReadAllText(filename);
            }
            catch (Exception e)
            {
                LogError("Read quicksave", e);
                return;
            }

            try
            {
                var saveDataFromString = Character.importExport.getSaveDataFromString(base64Data);
                var dataFromString = Character.importExport.getDataFromString(base64Data);

                if ((dataFromString == null || dataFromString.version < 361) &&
                    Application.platform != RuntimePlatform.WindowsEditor)
                {
                    Log("Bad save version");
                    return;
                }

                if (dataFromString.version > Character.getVersion())
                {
                    Log("Bad save version");
                    return;
                }

                Character.saveLoad.loadintoGame(saveDataFromString);
            }
            catch (Exception e)
            {
                LogError("Load quicksave", e);
            }
        }

        private void DumpEquipped()
        {
            var list = new List<int>
            {
                Character.inventory.head.id,
                Character.inventory.chest.id,
                Character.inventory.legs.id,
                Character.inventory.boots.id,
                Character.inventory.weapon.id
            };

            if (Character.inventoryController.weapon2Unlocked())
            {
                list.Add(Character.inventory.weapon2.id);
            }

            foreach (var acc in Character.inventory.accs)
            {
                list.Add(acc.id);
            }

            list.RemoveAll(x => x == 0);
            var items = $"[{string.Join(", ", list.Select(x => x.ToString()).ToArray())}]";

            Log($"Equipped Items: {items}");
            Clipboard.SetText(items);
        }

        public void OnGUI()
        {
            if (_unloaded || !_started || ExternalUiEnabled || Settings == null || Settings.DisableOverlay) return;
            var customActive = Settings.UseCustomRebirthPlan || Settings.CustomAdventureEnabled || Settings.ManualAdventureEnabled || Settings.CustomAutoMoneyPitEnabled || Settings.CustomAutoBoostEnabled || Settings.CustomAutoBloodSpellsEnabled;
            GUI.Label(new Rect(10, 0, 260, 40), $"Legacy - {(LegacyAutomationEnabled && Settings.GlobalEnabled ? "Active" : "Disabled")}");
            GUI.Label(new Rect(10, 10, 260, 40), $"Custom - {(customActive ? "Active" : "Inactive")}");
            GUI.Label(new Rect(10, 20, 260, 40), $"Next Loop - {_timeLeft:00.0}s");
            GUI.Label(new Rect(10, 30, 260, 40), $"Profile - {Settings.AllocationFile}");
        }

        public void OnApplicationQuit()
        {
            Loader.Unload();
        }

    }
}
