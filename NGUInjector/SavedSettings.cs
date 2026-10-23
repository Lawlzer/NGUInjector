using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using SimpleJSON;
using UnityEngine;

namespace NGUInjector
{
    [Serializable]
    public class IronPillHistoryEntry
    {
        public string CastAtUtc;
        public double WaitSeconds;
        public double Worth;
        public double RebirthSeconds;
    }

    [Serializable]
    public class SavedSettings
    {
        [SerializeField] private int _snipeZone = -1;
        [SerializeField] private bool _precastBuffs;
        [SerializeField] private bool _swapTitanLoadouts;
        [SerializeField] private bool _swapYggdrasilLoadouts;
        [SerializeField] private int[] _priorityBoosts;
        [SerializeField] private bool _manageEnergy;
        [SerializeField] private bool _manageMagic;
        [SerializeField] private bool _fastCombat;
        [SerializeField] private bool _manageGear;
        [SerializeField] private bool _manageDiggers;
        [SerializeField] private bool _upgradeDiggers;
        [SerializeField] private bool _manageYggdrasil;
        [SerializeField] private int[] _titanLoadout;
        [SerializeField] private int[] _yggdrasilLoadout;
        [SerializeField] private bool _manageInventory;
        [SerializeField] private bool _autoFight;
        [SerializeField] private bool _autoQuest;
        [SerializeField] private bool _allowMajorQuests;
        [SerializeField] private bool _autoConvertBoosts;
        [SerializeField] private bool _autoQuestItopod;
        [SerializeField] private int[] _goldDropLoadout;
        [SerializeField] private bool _autoMoneyPit;
        [SerializeField] private bool _autoSpin;
        [SerializeField] private int[] _moneyPitLoadout;
        [SerializeField] private bool _autoRebirth = true;
        [SerializeField] private bool _manageWandoos;
        [SerializeField] private double _moneyPitThreshold;
        [SerializeField] private bool _doGoldSwap;
        [SerializeField] private int[] _boostBlacklist;
        [SerializeField] private bool _snipeBossOnly;
        [SerializeField] private bool _recoverHealth;
        [SerializeField] private int _combatMode;
        [SerializeField] private bool _allowZoneFallback;
        [SerializeField] private bool _abandonMinors;
        [SerializeField] private int _minorAbandonThreshold;
        [SerializeField] private int _questCombatMode;
        [SerializeField] private bool _questFastCombat;
        [SerializeField] private bool _autoSpellSwap;
        [SerializeField] private int _spaghettiThreshold;
        [SerializeField] private int _counterfeitThreshold;
        [SerializeField] private bool _castBloodSpells;
        [SerializeField] private int _ironPillThreshold;
        [SerializeField] private int _bloodMacGuffinAThreshold;
        [SerializeField] private int _bloodMacGuffinBThreshold;
        [SerializeField] private bool _autoBuyEm;
        [SerializeField] private double _bloodNumberThreshold;
        [SerializeField] private int[] _quickLoadout;
        [SerializeField] private int[] _quickDiggers;
        [SerializeField] private bool _globalEnabled;
        [SerializeField] private bool _combatEnabled;
        [SerializeField] private bool _useButterMajor;
        [SerializeField] private bool _useButterMinor;
        [SerializeField] private bool _manualMinors;
        [SerializeField] private bool _manageR3;
        [SerializeField] private bool _activateFruits;
        [SerializeField] private int[] _wishPriorities;
        [SerializeField] private bool _wishSortPriorities;
        [SerializeField] private bool _wishSortOrder;        
        [SerializeField] private bool _beastMode;
        [SerializeField] private int _cubePriority;
        [SerializeField] private bool _manageNguDiff;
        [SerializeField] private int[] _energyNguWeights = {0, 0, 0, 0, 1, 0, 1, 0, 0};
        [SerializeField] private int[] _magicNguWeights = {1, 0, 0, 0, 0, 0, 0};
        [SerializeField] private string _allocationFile;
        [SerializeField] private bool _manageGoldLoadouts;
        [SerializeField] private int _resnipeTime;
        [SerializeField] private bool[] _titanMoneyDone;
        [SerializeField] private bool[] _titanGoldTargets;
        [SerializeField] private bool[] _titanSwapTargets;
        [SerializeField] private bool _goldCBlockMode;
        [SerializeField] private bool _debugAllocation;
        [SerializeField] private bool _optimizeItopodFloor;
        [SerializeField] private int _itopodCombatMode;
        [SerializeField] private bool _itopodBeastMode;
        [SerializeField] private bool _itopodFastCombat;
        [SerializeField] private bool _itopodRecoverHp;
        [SerializeField] private bool _itopodPrecastBuffs;
        [SerializeField] private bool _adventureTargetItopod;
        [SerializeField] private bool _disableOverlay;
        [SerializeField] private int _yggSwapThreshold;
        [SerializeField] private bool _moreBlockParry;
        [SerializeField] private int[] _specialBoostBlacklist;
        [SerializeField] private int[] _blacklistedBosses;
        [SerializeField] private bool _hackAdvance;
        [SerializeField] private bool _useCustomRebirthPlan;
        [SerializeField] private string _customPlanFile;
        [SerializeField] private bool _customAdventureEnabled;
        [SerializeField] private int _customAdventureMode;
        [SerializeField] private int _customAdventureMaxItopodDeaths = 3;
        [SerializeField] private int _customAdventureZone;
        [SerializeField] private bool _customAdventureGoldPush;
        [SerializeField] private AdventureRule[] _customAdventureRules = new AdventureRule[0];
        [SerializeField] private bool _customAutoMoneyPitEnabled;
        [SerializeField] private bool _customAutoExpEnabled;
        [SerializeField] private int _customExpEnergyRatio = 5;
        [SerializeField] private int _customExpMagicRatio = 1;
        [SerializeField] private int _customExpEnergyPowerRatio = 5;
        [SerializeField] private int _customExpEnergyCapRatio = 160000;
        [SerializeField] private int _customExpEnergyBarsRatio = 4;
        [SerializeField] private int _customExpMagicPowerRatio = 5;
        [SerializeField] private int _customExpMagicCapRatio = 160000;
        [SerializeField] private int _customExpMagicBarsRatio = 4;
        [SerializeField] private bool _customAutoBoostEnabled;
        [SerializeField] private int _customAutoBoostCubePowerRatio;
        [SerializeField] private int _customAugmentsBloodEstimateSeconds;
        [SerializeField] private bool _customAutoBloodSpellsEnabled;
        [SerializeField] private int _customBloodSpaghettiGoalPercent;
        [SerializeField] private int _customBloodCounterfeitGoalPercent;
        [SerializeField] private int _customIronPillSaveSeconds = 900;
        [SerializeField] private double _customIronPillMinWorth;
        [SerializeField] private IronPillHistoryEntry[] _customIronPillHistory = new IronPillHistoryEntry[0];
        [SerializeField] private bool _manualAdventureEnabled;
        [SerializeField] private int _manualAdventureZone;
        [SerializeField] private int _manualAdventureLoadout = -1;
        [SerializeField] private double[] _manualAdventureGearWeights = new double[5];
        [SerializeField] private double[] _manualAdventureBeardWeights = new double[7];
        [SerializeField] private double[] _manualAdventureDiggerWeights = new double[12];
        [SerializeField] private bool _manualAdventureBossOnly;
        [SerializeField] private bool _manualAdventureRecoverHealth = true;
        [SerializeField] private bool _manualAdventurePrecastBuffs;
        [SerializeField] private bool _manualAdventureFastCombat = true;
        [SerializeField] private bool _manualAdventureBeastMode;
        [SerializeField] private bool _manualAdventureAllowFallback = true;
        [SerializeField] private bool _manualTitanEnabled;
        [SerializeField] private int _manualTitanZone = 16;
        [SerializeField] private bool _manualTitanRequireAT = true;
        [SerializeField] private int _manualTitanLoadout = 1;
        [SerializeField] private int _manualTitanMaxDeaths = 3;

        private readonly string _savePath;
        private bool _disableSave;
        private string _lastAppliedText;
        private JSONObject _sourceJson;
        private string _loadedRevision = string.Empty;
        private string _liveBaseline;

        private static readonly FieldInfo[] PersistentFields = typeof(SavedSettings).GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(field => Attribute.IsDefined(field, typeof(SerializeField))).ToArray();

        public SavedSettings(string dir)
        {
            if (dir != null)
                _savePath = Path.Combine(dir, "settings.json");
            if (_savePath != null) _liveBaseline = SerializeWithUnknownFields(this);
        }

        private string ReadDisk(out string revision)
        {
            byte[] bytes;
            try { bytes = File.ReadAllBytes(_savePath); }
            catch (IOException error) when (error is FileNotFoundException || error is DirectoryNotFoundException)
            {
                try { File.GetAttributes(_savePath); }
                catch (FileNotFoundException) { revision = string.Empty; return null; }
                catch (DirectoryNotFoundException) { revision = string.Empty; return null; }
                throw new IOException("Settings path exists but cannot be read.", error);
            }
            revision = JsonFiles.ComputeRevision(bytes);
            var offset = bytes.Length >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf ? 3 : 0;
            var text = new UTF8Encoding(false, true).GetString(bytes, offset, bytes.Length - offset);
            JsonFiles.ParseObject(text);
            return text;
        }

        private void RequireUnchangedDisk(string expectedRevision = null)
        {
            ReadDisk(out var revision);
            if (revision != _loadedRevision || expectedRevision != null && revision != expectedRevision)
                throw new InvalidOperationException("Settings revision conflict; explicitly reload settings before saving.");
        }

        private SavedSettings CopyPersistent()
        {
            var copy = new SavedSettings(null);
            foreach (var field in PersistentFields)
            {
                var value = field.GetValue(this);
                field.SetValue(copy, value is Array array ? array.Clone() : value);
            }
            return copy;
        }

        private void Commit(SavedSettings candidate, bool updateForm = true)
        {
            RequireUnchangedDisk();
            var serialized = SerializeWithUnknownFields(candidate);
            JsonFiles.WriteAtomic(_savePath, serialized);
            var runChanged = _customPlanFile != candidate._customPlanFile || _useCustomRebirthPlan != candidate._useCustomRebirthPlan || _autoRebirth != candidate._autoRebirth;
            foreach (var field in PersistentFields) field.SetValue(this, field.GetValue(candidate));
            _lastAppliedText = serialized;
            _loadedRevision = JsonFiles.ComputeRevision(new UTF8Encoding(false).GetBytes(serialized));
            _sourceJson = JsonFiles.ParseObject(serialized);
            _liveBaseline = SerializeWithUnknownFields(this);
            if (runChanged) NotifyCustomPlanRunChanged();
            if (updateForm) Main.UpdateForm(this);
        }

        private void SetPersistent(string fieldName, object value)
        {
            var field = PersistentFields.Single(item => item.Name == fieldName);
            if (_savePath == null || _disableSave) { field.SetValue(this, value); return; }
            lock (CustomPlans.PresetLibraryStore.SyncRoot)
            {
                var candidate = CopyPersistent();
                field.SetValue(candidate, value is Array array ? array.Clone() : value);
                Main.Log("Saving Settings");
                Commit(candidate);
            }
        }

        internal JSONObject ApplyExternalPatch(JSONNode patch, string expectedRevision)
        {
            if (expectedRevision == null || expectedRevision.Length != 0 && (expectedRevision.Length != 64
                || expectedRevision.Any(c => !(c >= '0' && c <= '9' || c >= 'a' && c <= 'f'))))
                throw new FormatException("args.expectedRevision is required: lowercase SHA-256, or empty only for an absent settings file.");
            lock (CustomPlans.PresetLibraryStore.SyncRoot)
            {
                RequireUnchangedDisk(expectedRevision);
                if (SerializeWithUnknownFields(this) != _liveBaseline)
                    throw new InvalidOperationException("Unsaved native settings exist; explicitly save or reload settings before external editing.");
                ApplyPatch(patch);
                var result = new JSONObject();
                result["revision"] = _loadedRevision;
                result["settings"] = JsonFiles.ParseObject(_lastAppliedText);
                return result;
            }
        }

        internal void SaveSettings()
        {
            if (_savePath == null) return;
            if (_disableSave) return;
            lock (CustomPlans.PresetLibraryStore.SyncRoot)
            {
                Main.Log("Saving Settings");
                Commit(CopyPersistent());
            }
        }

        internal void ApplyPatch(JSONNode patch)
        {
            lock (CustomPlans.PresetLibraryStore.SyncRoot) ApplyPatchCore(patch);
        }

        private void ApplyPatchCore(JSONNode patch)
        {
            if (patch == null || !patch.IsObject)
                throw new FormatException("args.patch must be a JSON object using serialized settings field names.");
            if (_savePath == null || _disableSave)
                throw new InvalidOperationException("Settings saving is unavailable.");
            RequireUnchangedDisk();

            var merged = JsonFiles.ParseObject(JsonUtility.ToJson(this));
            merged["_manualAdventureGearWeights"] = SerializeManualAdventureWeights(ManualAdventureGearWeights, "_manualAdventureGearWeights", 5);
            merged["_manualAdventureBeardWeights"] = SerializeManualAdventureWeights(ManualAdventureBeardWeights, "_manualAdventureBeardWeights", 7);
            merged["_manualAdventureDiggerWeights"] = SerializeManualAdventureWeights(ManualAdventureDiggerWeights, "_manualAdventureDiggerWeights", 12);
            var adventureRules = CustomAdventureRules;
            foreach (var item in patch.Linq)
            {
                if (item.Key == "_energyNguWeights" || item.Key == "_magicNguWeights")
                    CustomPlans.CustomPlanStore.ReadNguWeights(item.Value, item.Key, item.Key == "_energyNguWeights" ? 9 : 7);
                if (item.Key == "_manualAdventureGearWeights" || item.Key == "_manualAdventureBeardWeights" || item.Key == "_manualAdventureDiggerWeights")
                    ReadManualAdventureWeights(item.Value, item.Key, item.Key == "_manualAdventureGearWeights" ? 5 : item.Key == "_manualAdventureBeardWeights" ? 7 : 12);
                if (item.Key == "_customIronPillHistory")
                    throw new FormatException("Iron Pill history is backend-owned and read-only.");
                if (item.Key == "_customAdventureRules")
                {
                    if (!item.Value.IsArray)
                        throw new FormatException("_customAdventureRules must be an array.");
                    adventureRules = AdventureRule.Parse(item.Value);
                    continue;
                }
                var field = typeof(SavedSettings).GetField(item.Key, BindingFlags.NonPublic | BindingFlags.Instance);
                if (field == null || !Attribute.IsDefined(field, typeof(SerializeField)))
                    throw new FormatException("Unknown settings field: " + item.Key);
                var type = field.FieldType;
                var values = type.IsArray && item.Value.IsArray ? item.Value.Children.ToArray() : new[] {item.Value};
                if (type.IsArray && !item.Value.IsArray)
                    throw new FormatException(item.Key + " must be an array.");
                var elementType = type.IsArray ? type.GetElementType() : type;
                foreach (var value in values)
                {
                    var valid = elementType == typeof(bool) ? value.IsBoolean
                        : elementType == typeof(string) ? value.IsString
                        : value.IsNumber && !double.IsNaN(value.AsDouble) && !double.IsInfinity(value.AsDouble)
                          && (elementType != typeof(int) || IsExactInt32(value));
                    if (!valid)
                        throw new FormatException("Invalid value type for " + item.Key);
                }
                if (item.Key == "_allocationFile" || item.Key == "_customPlanFile")
                {
                    var name = item.Value.Value;
                    if (string.IsNullOrWhiteSpace(name) || name != name.Trim(' ', '.') || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                        throw new FormatException("Invalid file stem for " + item.Key);
                }
                if (item.Key == "_customIronPillSaveSeconds" && (item.Value.AsDouble < 0d || item.Value.AsDouble > 604800d)
                    || item.Key == "_customIronPillMinWorth" && (item.Value.AsDouble < 0d || item.Value.AsDouble > 1e8))
                    throw new FormatException("Iron Pill threshold is out of range: " + item.Key);
                if (item.Key == "_customAutoBoostCubePowerRatio" && (item.Value.AsDouble < 1d || item.Value.AsDouble > 4d)
                    || item.Key == "_customAugmentsBloodEstimateSeconds" && (item.Value.AsDouble < 1d || item.Value.AsDouble > 100000d)
                    || (item.Key == "_customBloodSpaghettiGoalPercent" || item.Key == "_customBloodCounterfeitGoalPercent")
                       && (item.Value.AsDouble < 0d || item.Value.AsDouble > 100000d))
                    throw new FormatException("Custom setting is out of range: " + item.Key);
                if (item.Key.StartsWith("_customExp", StringComparison.Ordinal)
                    && (item.Value.AsDouble < 1d || item.Value.AsDouble > 1000000000d))
                    throw new FormatException("EXP ratios must be whole numbers between 1 and 1000000000: " + item.Key);
                if (item.Key == "_customAdventureMode" && (item.Value.AsDouble < 0 || item.Value.AsDouble > 3)
                    || item.Key == "_customAdventureMaxItopodDeaths" && (item.Value.AsDouble < 1 || item.Value.AsDouble > 1000))
                    throw new FormatException("Adventure setting is out of range: " + item.Key);
                if (item.Key == "_manualAdventureLoadout" || item.Key == "_manualTitanLoadout"
                    || item.Key == "_manualTitanMaxDeaths" || item.Key == "_manualTitanZone")
                {
                    // Validate the source token, not a fraction/underflow rounded to an integer by double.
                    int manualValue;
                    if (!int.TryParse(item.Value.Value, System.Globalization.NumberStyles.AllowLeadingSign,
                            System.Globalization.CultureInfo.InvariantCulture, out manualValue)
                        || (item.Key == "_manualAdventureLoadout" || item.Key == "_manualTitanLoadout") && (manualValue < -1 || manualValue > 2)
                        || item.Key == "_manualTitanMaxDeaths" && (manualValue < 1 || manualValue > 1000)
                        || item.Key == "_manualTitanZone" && !Managers.ZoneHelpers.TitanZones.Contains(manualValue))
                        throw new FormatException("Manual combat setting is out of range: " + item.Key);
                }
                if (item.Key == "_titanGoldTargets" || item.Key == "_titanSwapTargets" || item.Key == "_titanMoneyDone")
                {
                    if (item.Value.Count != Managers.ZoneHelpers.TitanZones.Length)
                        throw new FormatException(item.Key + " must have one entry per titan.");
                }
                merged[item.Key] = item.Value;
            }

            // Merge against live state, not a UI snapshot; persist before publishing the candidate.
            merged.Remove("_customIronPillHistory");
            merged.Remove("_customAdventureRules");
            var candidate = new SavedSettings(null);
            var unityCandidate = JsonFiles.ParseObject(merged.ToString());
            unityCandidate.Remove("_energyNguWeights");
            unityCandidate.Remove("_magicNguWeights");
            unityCandidate.Remove("_manualAdventureGearWeights");
            unityCandidate.Remove("_manualAdventureBeardWeights");
            unityCandidate.Remove("_manualAdventureDiggerWeights");
            var parsed = JsonUtility.FromJson<SavedSettings>(unityCandidate.ToString());
            parsed._energyNguWeights = CustomPlans.CustomPlanStore.ReadNguWeights(merged["_energyNguWeights"], "_energyNguWeights", 9);
            parsed._magicNguWeights = CustomPlans.CustomPlanStore.ReadNguWeights(merged["_magicNguWeights"], "_magicNguWeights", 7);
            parsed._manualAdventureGearWeights = ReadManualAdventureWeights(merged["_manualAdventureGearWeights"], "_manualAdventureGearWeights", 5);
            parsed._manualAdventureBeardWeights = ReadManualAdventureWeights(merged["_manualAdventureBeardWeights"], "_manualAdventureBeardWeights", 7);
            parsed._manualAdventureDiggerWeights = ReadManualAdventureWeights(merged["_manualAdventureDiggerWeights"], "_manualAdventureDiggerWeights", 12);
            candidate.MassUpdate(parsed);
            candidate._customIronPillHistory = CustomIronPillHistory.Where(IsValidIronPillHistoryEntry).Take(10).ToArray();
            candidate._customAdventureRules = AdventureRule.Parse(AdventureRule.ToJson(adventureRules));
            Commit(candidate);
        }

        private static bool IsExactInt32(JSONNode value)
        {
            var parts = value.Value.Split(new[] {'e', 'E'});
            var point = parts[0].IndexOf('.');
            var digits = parts[0].Replace(".", "").TrimStart('-').TrimStart('0');
            if (digits.Length == 0) return true;
            long exponent = 0;
            if (parts.Length > 1 && !long.TryParse(parts[1], System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out exponent)) return false;
            var significant = digits.TrimEnd('0');
            var fractional = point < 0 ? 0 : parts[0].Length - point - 1;
            var adjustment = (long) digits.Length - significant.Length - fractional;
            if (exponent < -adjustment || exponent > 10 - significant.Length - adjustment) return false;
            var integer = (parts[0][0] == '-' ? "-" : "") + significant + new string('0', (int) (exponent + adjustment));
            return int.TryParse(integer, System.Globalization.NumberStyles.AllowLeadingSign,
                System.Globalization.CultureInfo.InvariantCulture, out _);
        }

        private string SerializeWithUnknownFields(SavedSettings live)
        {
            var root = JsonFiles.ParseObject(_sourceJson?.ToString() ?? "{}");
            foreach (var item in JsonFiles.ParseObject(JsonUtility.ToJson(live)).Linq)
                root[item.Key] = item.Value;
            // Injected custom record arrays are not reliably supported by Unity's serializer.
            var history = new JSONArray();
            var sourceHistory = _sourceJson?["_customIronPillHistory"]?.Children.ToList() ?? new List<JSONNode>();
            foreach (var entry in live.CustomIronPillHistory.Where(IsValidIronPillHistoryEntry).Take(10))
            {
                // Retain extensions on unchanged history rows, including after a prepend.
                var retained = sourceHistory.FirstOrDefault(source => source.IsObject && source["CastAtUtc"].IsString
                    && source["CastAtUtc"].Value == entry.CastAtUtc && source["WaitSeconds"].IsNumber && source["WaitSeconds"].AsDouble == entry.WaitSeconds
                    && source["Worth"].IsNumber && source["Worth"].AsDouble == entry.Worth
                    && source["RebirthSeconds"].IsNumber && source["RebirthSeconds"].AsDouble == entry.RebirthSeconds);
                if (retained != null)
                {
                    history.Add(JsonFiles.ParseObject(retained.ToString()));
                    sourceHistory.Remove(retained);
                    continue;
                }
                var row = new JSONObject();
                row["CastAtUtc"] = entry.CastAtUtc ?? string.Empty;
                row["WaitSeconds"] = entry.WaitSeconds;
                row["Worth"] = entry.Worth;
                row["RebirthSeconds"] = entry.RebirthSeconds;
                history.Add(row);
            }
            root["_customIronPillHistory"] = history;
            root["_customAdventureRules"] = AdventureRule.ToJson(live.CustomAdventureRules);
            root["_energyNguWeights"] = CustomPlans.CustomPlanStore.SerializeNguWeights(live._energyNguWeights, 9, "_energyNguWeights");
            root["_magicNguWeights"] = CustomPlans.CustomPlanStore.SerializeNguWeights(live._magicNguWeights, 7, "_magicNguWeights");
            root["_manualAdventureGearWeights"] = SerializeManualAdventureWeights(live.ManualAdventureGearWeights, "_manualAdventureGearWeights", 5);
            root["_manualAdventureBeardWeights"] = SerializeManualAdventureWeights(live.ManualAdventureBeardWeights, "_manualAdventureBeardWeights", 7);
            root["_manualAdventureDiggerWeights"] = SerializeManualAdventureWeights(live.ManualAdventureDiggerWeights, "_manualAdventureDiggerWeights", 12);
            return root.ToString(2);
        }

        private static double[] ReadManualAdventureWeights(JSONNode node, string key, int count)
        {
            if (node == null || !node.IsArray || node.Count != count || node.Children.Any(value => !value.IsNumber))
                throw new FormatException(key + " must contain exactly " + count + " finite numbers between 0 and 1000000.");
            var weights = node.Children.Select(value => value.AsDouble).ToArray();
            SerializeManualAdventureWeights(weights, key, count);
            return weights;
        }

        private static JSONArray SerializeManualAdventureWeights(double[] weights, string key, int count)
        {
            if (weights == null || weights.Length != count || weights.Any(value => !(value >= 0d && value <= 1000000d)))
                throw new FormatException(key + " must contain exactly " + count + " finite numbers between 0 and 1000000.");
            var array = new JSONArray();
            foreach (var value in weights) array.Add(value);
            return array;
        }

        private static bool IsValidIronPillHistoryEntry(IronPillHistoryEntry entry)
        {
            // These inclusive ranges also reject NaN and infinities.
            return entry != null && entry.WaitSeconds >= 0d && entry.WaitSeconds <= double.MaxValue
                   && entry.RebirthSeconds >= 0d && entry.RebirthSeconds <= double.MaxValue
                   && entry.Worth >= 0d && entry.Worth <= 1e8d;
        }

        private static IronPillHistoryEntry[] ReadIronPillHistory(JSONNode node)
        {
            if (node == null || node.IsNull) return new IronPillHistoryEntry[0];
            if (!node.IsArray) throw new FormatException("Iron Pill history must be an array.");
            var entries = new List<IronPillHistoryEntry>();
            foreach (var row in node.Children)
            {
                if (!row.IsObject || !row["CastAtUtc"].IsString || !row["WaitSeconds"].IsNumber
                    || !row["Worth"].IsNumber || !row["RebirthSeconds"].IsNumber)
                    continue;
                var entry = new IronPillHistoryEntry {
                    CastAtUtc = row["CastAtUtc"].Value,
                    WaitSeconds = row["WaitSeconds"].AsDouble,
                    Worth = row["Worth"].AsDouble,
                    RebirthSeconds = row["RebirthSeconds"].AsDouble
                };
                if (!IsValidIronPillHistoryEntry(entry)) continue;
                entries.Add(entry);
                if (entries.Count == 10) break;
            }
            return entries.ToArray();
        }

        internal void SetSaveDisabled(bool disabled)
        {
            _disableSave = disabled;
        }

        internal bool HasSettingsFileChanged()
        {
            ReadDisk(out var revision);
            return revision != _loadedRevision;
        }

        internal bool LoadSettings()
        {
            lock (CustomPlans.PresetLibraryStore.SyncRoot) return LoadSettingsCore();
        }

        private bool LoadSettingsCore()
        {
            if (File.Exists(_savePath))
            {
                try
                {
                    var text = ReadDisk(out var revision);
                    if (text == null) return false;
                    if (revision == _loadedRevision && SerializeWithUnknownFields(this) == _liveBaseline)
                        return true;
                    var root = JsonFiles.ParseObject(text);
                    var hasEnergyNguWeights = root.Linq.Any(item => item.Key == "_energyNguWeights");
                    var hasMagicNguWeights = root.Linq.Any(item => item.Key == "_magicNguWeights");
                    JSONObject legacyPlan = null;
                    // Read the selected raw plan before any store construction can repair it.
                    // Once initialized in this process, removing a global key cannot replay migration.
                    if ((!hasEnergyNguWeights || !hasMagicNguWeights) && _sourceJson == null)
                    {
                        var selected = root["_customPlanFile"];
                        if (selected != null && selected.IsString && !string.IsNullOrWhiteSpace(selected.Value))
                        {
                            var name = selected.Value;
                            if (name != Path.GetFileName(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                                throw new FormatException("Invalid selected custom plan file.");
                            var path = Path.Combine(Path.GetDirectoryName(_savePath), "custom-plans", name + ".json");
                            if (File.Exists(path)) legacyPlan = JsonFiles.ParseObject(File.ReadAllText(path));
                        }
                    }
                    var energyWeights = hasEnergyNguWeights
                        ? CustomPlans.CustomPlanStore.ReadNguWeights(root["_energyNguWeights"], "_energyNguWeights", 9)
                        : legacyPlan != null ? CustomPlans.CustomPlanStore.ReadLegacyNguWeights(legacyPlan, true) : EnergyNguWeights;
                    var magicWeights = hasMagicNguWeights
                        ? CustomPlans.CustomPlanStore.ReadNguWeights(root["_magicNguWeights"], "_magicNguWeights", 7)
                        : legacyPlan != null ? CustomPlans.CustomPlanStore.ReadLegacyNguWeights(legacyPlan, false) : MagicNguWeights;
                    var hasCustomAdventureGoldPush = root["_customAdventureGoldPush"] != null;
                    var hasCustomAutoMoneyPitEnabled = root["_customAutoMoneyPitEnabled"] != null;
                    var hasCustomAutoBoostEnabled = root["_customAutoBoostEnabled"] != null;
                    var hasCustomAutoBoostCubePowerRatio = root["_customAutoBoostCubePowerRatio"] != null;
                    var hasCustomAugmentsBloodEstimateSeconds = root["_customAugmentsBloodEstimateSeconds"] != null;
                    var hasCustomAutoBloodSpellsEnabled = root["_customAutoBloodSpellsEnabled"] != null;
                    var hasCustomBloodSpaghettiGoalPercent = root["_customBloodSpaghettiGoalPercent"] != null;
                    var hasCustomBloodCounterfeitGoalPercent = root["_customBloodCounterfeitGoalPercent"] != null;
                    var hasManualAdventureRecoverHealth = root["_manualAdventureRecoverHealth"] != null;
                    var hasManualAdventureFastCombat = root["_manualAdventureFastCombat"] != null;
                    var hasManualAdventureAllowFallback = root["_manualAdventureAllowFallback"] != null;
                    var history = ReadIronPillHistory(root["_customIronPillHistory"]);
                    var adventureRules = AdventureRule.Parse(root["_customAdventureRules"]);
                    var gearWeights = root.Linq.Any(item => item.Key == "_manualAdventureGearWeights")
                        ? ReadManualAdventureWeights(root["_manualAdventureGearWeights"], "_manualAdventureGearWeights", 5) : new double[5];
                    var beardWeights = root.Linq.Any(item => item.Key == "_manualAdventureBeardWeights")
                        ? ReadManualAdventureWeights(root["_manualAdventureBeardWeights"], "_manualAdventureBeardWeights", 7) : new double[7];
                    var diggerWeights = root.Linq.Any(item => item.Key == "_manualAdventureDiggerWeights")
                        ? ReadManualAdventureWeights(root["_manualAdventureDiggerWeights"], "_manualAdventureDiggerWeights", 12) : new double[12];
                    var unityRoot = JsonFiles.ParseObject(text);
                    unityRoot.Remove("_customIronPillHistory");
                    unityRoot.Remove("_customAdventureRules");
                    unityRoot.Remove("_energyNguWeights");
                    unityRoot.Remove("_magicNguWeights");
                    unityRoot.Remove("_manualAdventureGearWeights");
                    unityRoot.Remove("_manualAdventureBeardWeights");
                    unityRoot.Remove("_manualAdventureDiggerWeights");
                    var newSettings = JsonUtility.FromJson<SavedSettings>(unityRoot.ToString());
                    // Unity does not apply field initializers to missing serialized fields.
                    if (root["_autoRebirth"] == null)
                        newSettings._autoRebirth = true;
                    if (root["_manualAdventureLoadout"] == null)
                        newSettings._manualAdventureLoadout = -1;
                    if (root["_manualTitanZone"] == null)
                        newSettings._manualTitanZone = 16;
                    if (root["_manualTitanRequireAT"] == null)
                        newSettings._manualTitanRequireAT = true;
                    if (root["_manualTitanLoadout"] == null)
                        newSettings._manualTitanLoadout = 1;
                    if (root["_manualTitanMaxDeaths"] == null)
                        newSettings._manualTitanMaxDeaths = 3;
                    newSettings._customIronPillHistory = history;
                    newSettings._customAdventureRules = adventureRules;
                    newSettings._energyNguWeights = energyWeights;
                    newSettings._magicNguWeights = magicWeights;
                    newSettings._manualAdventureGearWeights = gearWeights;
                    newSettings._manualAdventureBeardWeights = beardWeights;
                    newSettings._manualAdventureDiggerWeights = diggerWeights;
                    if (!hasEnergyNguWeights || !hasMagicNguWeights)
                    {
                        if (_disableSave) throw new InvalidOperationException("NGU settings migration requires saving to be enabled.");
                        root["_energyNguWeights"] = CustomPlans.CustomPlanStore.SerializeNguWeights(energyWeights, 9, "_energyNguWeights");
                        root["_magicNguWeights"] = CustomPlans.CustomPlanStore.SerializeNguWeights(magicWeights, 7, "_magicNguWeights");
                        text = root.ToString(2);
                        ReadDisk(out var currentRevision);
                        if (currentRevision != revision) throw new InvalidOperationException("Settings changed during migration; explicitly reload settings.");
                        JsonFiles.WriteAtomic(_savePath, text);
                        revision = JsonFiles.ComputeRevision(new UTF8Encoding(false).GetBytes(text));
                    }
                    MassUpdate(newSettings);
                    if (root["_customIronPillSaveSeconds"] == null)
                        _customIronPillSaveSeconds = 900;
                    if (root["_customAdventureMaxItopodDeaths"] == null)
                        _customAdventureMaxItopodDeaths = 3;
                    if (!hasCustomAdventureGoldPush)
                    {
                        _customAdventureGoldPush = true;
                    }

                    if (!hasCustomAutoMoneyPitEnabled)
                    {
                        _customAutoMoneyPitEnabled = false;
                    }

                    if (!hasCustomAutoBoostEnabled)
                    {
                        _customAutoBoostEnabled = true;
                    }

                    if (!hasCustomAutoBoostCubePowerRatio || _customAutoBoostCubePowerRatio < 1)
                    {
                        _customAutoBoostCubePowerRatio = 2;
                    }

                    if (!hasCustomAugmentsBloodEstimateSeconds || _customAugmentsBloodEstimateSeconds < 1)
                    {
                        _customAugmentsBloodEstimateSeconds = 300;
                    }

                    if (!hasCustomAutoBloodSpellsEnabled)
                    {
                        _customAutoBloodSpellsEnabled = true;
                    }

                    if (!hasCustomBloodSpaghettiGoalPercent || _customBloodSpaghettiGoalPercent < 0)
                    {
                        _customBloodSpaghettiGoalPercent = 30;
                    }

                    if (!hasCustomBloodCounterfeitGoalPercent || _customBloodCounterfeitGoalPercent < 0)
                    {
                        _customBloodCounterfeitGoalPercent = 400;
                    }

                    if (!hasManualAdventureRecoverHealth)
                    {
                        _manualAdventureRecoverHealth = true;
                    }

                    if (!hasManualAdventureFastCombat)
                    {
                        _manualAdventureFastCombat = true;
                    }

                    if (!hasManualAdventureAllowFallback)
                    {
                        _manualAdventureAllowFallback = true;
                    }

                    Main.Log("Loaded Settings");
                    Main.Log(JsonUtility.ToJson(this, true));
                    _lastAppliedText = text;
                    _sourceJson = root;
                    _loadedRevision = revision;
                    _liveBaseline = SerializeWithUnknownFields(this);
                    return true;
                }
                catch (Exception e)
                {
                    Main.Log(e.Message);
                    Main.Log(e.StackTrace);
                    return false;
                }
            }

            return false;
        }

        internal void EnsureGlobalNguWeightsSaved()
        {
            // Fresh settings must also be durable before plan/preset stores can remove old arrays.
            if (_sourceJson == null) SaveSettings();
        }

        internal void MassUpdate(SavedSettings other)
        {
            var adventureRules = AdventureRule.Parse(AdventureRule.ToJson(other.CustomAdventureRules));
            var energyWeights = CustomPlans.CustomPlanStore.SerializeNguWeights(other._energyNguWeights, 9, "_energyNguWeights");
            var magicWeights = CustomPlans.CustomPlanStore.SerializeNguWeights(other._magicNguWeights, 7, "_magicNguWeights");
            var planRunChanged = _customPlanFile != other.CustomPlanFile
                                 || _useCustomRebirthPlan != other.UseCustomRebirthPlan
                                 || _autoRebirth != other.AutoRebirth;
            _priorityBoosts = other.PriorityBoosts;
            _boostBlacklist = other.BoostBlacklist;

            _yggdrasilLoadout = other.YggdrasilLoadout;
            _swapYggdrasilLoadouts = other.SwapYggdrasilLoadouts;

            _swapTitanLoadouts = other.SwapTitanLoadouts;
            _titanLoadout = other.TitanLoadout;

            _manageDiggers = other.ManageDiggers;
            _manageYggdrasil = other.ManageYggdrasil;
            _manageEnergy = other.ManageEnergy;
            _manageMagic = other.ManageMagic;
            _manageInventory = other.ManageInventory;
            _manageGear = other.ManageGear;
            _manageWandoos = other.ManageWandoos;
            _autoConvertBoosts = other.AutoConvertBoosts;

            _snipeZone = other.SnipeZone;
            _fastCombat = other.FastCombat;
            _precastBuffs = other.PrecastBuffs;

            _autoFight = other.AutoFight;

            _autoQuest = other.AutoQuest;
            _autoQuestItopod = other.AutoQuestITOPOD;
            _allowMajorQuests = other.AllowMajorQuests;

            _goldDropLoadout = other.GoldDropLoadout;

            _autoMoneyPit = other.AutoMoneyPit;
            _autoSpin = other.AutoSpin;
            _moneyPitLoadout = other.MoneyPitLoadout;
            _moneyPitThreshold = other.MoneyPitThreshold;

            _autoRebirth = other.AutoRebirth;
            _manageWandoos = other.ManageWandoos;
            _doGoldSwap = other.DoGoldSwap;

            _combatMode = other.CombatMode;
            _recoverHealth = other.RecoverHealth;
            _snipeBossOnly = other.SnipeBossOnly;
            _allowZoneFallback = other.AllowZoneFallback;
            _abandonMinors = other.AbandonMinors;
            _minorAbandonThreshold = other.MinorAbandonThreshold;
            _questCombatMode = other.QuestCombatMode;
            _questFastCombat = other.QuestFastCombat;
            _autoSpellSwap = other.AutoSpellSwap;
            _counterfeitThreshold = other.CounterfeitThreshold;
            _spaghettiThreshold = other.SpaghettiThreshold;
            _castBloodSpells = other.CastBloodSpells;
            _ironPillThreshold = other.IronPillThreshold;
            _bloodMacGuffinAThreshold = other.BloodMacGuffinAThreshold;
            _bloodMacGuffinBThreshold = other.BloodMacGuffinBThreshold;
            _autoBuyEm = other.AutoBuyEM;
            _bloodNumberThreshold = other.BloodNumberThreshold;
            _quickDiggers = other.QuickDiggers;
            _quickLoadout = other.QuickLoadout;
            _combatEnabled = other.CombatEnabled;
            _globalEnabled = other.GlobalEnabled;
            _useButterMajor = other.UseButterMajor;
            _useButterMinor = other.UseButterMinor;
            _manualMinors = other.ManualMinors;
            _manageR3 = other.ManageR3;
            _activateFruits = other.ActivateFruits;
            _wishPriorities = other.WishPriorities;
            _wishSortPriorities = other.WishSortPriorities;
            _wishSortOrder = other.WishSortOrder;
            _beastMode = other.BeastMode;
            _cubePriority = other.CubePriority;
            _manageNguDiff = other.ManageNGUDiff;
            _energyNguWeights = energyWeights.Children.Select(weight => weight.AsInt).ToArray();
            _magicNguWeights = magicWeights.Children.Select(weight => weight.AsInt).ToArray();
            _allocationFile = other.AllocationFile;
            _manageGoldLoadouts = other.ManageGoldLoadouts;
            _titanGoldTargets = other.TitanGoldTargets;
            _titanSwapTargets = other.TitanSwapTargets;
            _titanMoneyDone = other.TitanMoneyDone;
            _resnipeTime = other.ResnipeTime;
            _goldCBlockMode = other.GoldCBlockMode;
            _debugAllocation = other.DebugAllocation;
            _optimizeItopodFloor = other.OptimizeITOPODFloor;
            _adventureTargetItopod = other.AdventureTargetITOPOD;
            _itopodBeastMode = other.ITOPODBeastMode;
            _itopodCombatMode = other.ITOPODCombatMode;
            _itopodFastCombat = other.ITOPODFastCombat;
            _itopodPrecastBuffs = other.ITOPODPrecastBuffs;
            _itopodRecoverHp = other.ITOPODRecoverHP;
            _disableOverlay = other.DisableOverlay;
            _upgradeDiggers = other._upgradeDiggers;
            _yggSwapThreshold = other.YggSwapThreshold;
            _moreBlockParry = other.MoreBlockParry;
            _specialBoostBlacklist = other.SpecialBoostBlacklist;
            _blacklistedBosses = other.BlacklistedBosses;
            _hackAdvance = other.HackAdvance;
            _useCustomRebirthPlan = other.UseCustomRebirthPlan;
            _customPlanFile = other.CustomPlanFile;
            _customAdventureEnabled = other.CustomAdventureEnabled;
            _customAdventureMode = other.CustomAdventureMode;
            _customAdventureMaxItopodDeaths = other.CustomAdventureMaxItopodDeaths;
            _customAdventureZone = other.CustomAdventureZone;
            _customAdventureGoldPush = other.CustomAdventureGoldPush;
            _customAdventureRules = adventureRules;
            _customAutoMoneyPitEnabled = other.CustomAutoMoneyPitEnabled;
            _customAutoExpEnabled = other.CustomAutoExpEnabled;
            _customExpEnergyRatio = other.CustomExpEnergyRatio;
            _customExpMagicRatio = other.CustomExpMagicRatio;
            _customExpEnergyPowerRatio = other.CustomExpEnergyPowerRatio;
            _customExpEnergyCapRatio = other.CustomExpEnergyCapRatio;
            _customExpEnergyBarsRatio = other.CustomExpEnergyBarsRatio;
            _customExpMagicPowerRatio = other.CustomExpMagicPowerRatio;
            _customExpMagicCapRatio = other.CustomExpMagicCapRatio;
            _customExpMagicBarsRatio = other.CustomExpMagicBarsRatio;
            _customAutoBoostEnabled = other.CustomAutoBoostEnabled;
            _customAutoBoostCubePowerRatio = other.CustomAutoBoostCubePowerRatio;
            _customAugmentsBloodEstimateSeconds = other.CustomAugmentsBloodEstimateSeconds;
            _customAutoBloodSpellsEnabled = other.CustomAutoBloodSpellsEnabled;
            _customBloodSpaghettiGoalPercent = other.CustomBloodSpaghettiGoalPercent;
            _customBloodCounterfeitGoalPercent = other.CustomBloodCounterfeitGoalPercent;
            _customIronPillSaveSeconds = other.CustomIronPillSaveSeconds;
            _customIronPillMinWorth = other.CustomIronPillMinWorth;
            _customIronPillHistory = other.CustomIronPillHistory.Where(IsValidIronPillHistoryEntry).Take(10).ToArray();
            _manualAdventureEnabled = other.ManualAdventureEnabled;
            _manualAdventureZone = other.ManualAdventureZone;
            _manualAdventureLoadout = other.ManualAdventureLoadout;
            _manualAdventureGearWeights = other.ManualAdventureGearWeights;
            _manualAdventureBeardWeights = other.ManualAdventureBeardWeights;
            _manualAdventureDiggerWeights = other.ManualAdventureDiggerWeights;
            _manualAdventureBossOnly = other.ManualAdventureBossOnly;
            _manualAdventureRecoverHealth = other.ManualAdventureRecoverHealth;
            _manualAdventurePrecastBuffs = other.ManualAdventurePrecastBuffs;
            _manualAdventureFastCombat = other.ManualAdventureFastCombat;
            _manualAdventureBeastMode = other.ManualAdventureBeastMode;
            _manualAdventureAllowFallback = other.ManualAdventureAllowFallback;
            _manualTitanEnabled = other.ManualTitanEnabled;
            _manualTitanZone = other.ManualTitanZone;
            _manualTitanRequireAT = other.ManualTitanRequireAT;
            _manualTitanLoadout = other.ManualTitanLoadout;
            _manualTitanMaxDeaths = other.ManualTitanMaxDeaths;
            if (planRunChanged)
                NotifyCustomPlanRunChanged();
        }

        private void NotifyCustomPlanRunChanged()
        {
            // Candidate settings used for validation must not reset the live scheduler.
            if (ReferenceEquals(Main.Settings, this))
                Main.reference?.InvalidateCustomRuntimePlanCache();
        }

        public int SnipeZone
        {
            get => _snipeZone;
            set
            {
                if (value == _snipeZone) return;
                SetPersistent("_snipeZone", value);
            }
        }

        public bool PrecastBuffs
        {
            get => _precastBuffs;
            set
            {
                if (value == _precastBuffs) return;
                SetPersistent("_precastBuffs", value);
            }
        }

        public bool SwapTitanLoadouts
        {
            get => _swapTitanLoadouts;
            set
            {
                if (value == _swapTitanLoadouts) return;
                SetPersistent("_swapTitanLoadouts", value);
            }
        }

        public bool SwapYggdrasilLoadouts
        {
            get => _swapYggdrasilLoadouts;
            set
            {
                if (value == _swapYggdrasilLoadouts) return;
                SetPersistent("_swapYggdrasilLoadouts", value);
            }
        }

        public int[] PriorityBoosts
        {
            get => _priorityBoosts;
            set
            {
                SetPersistent("_priorityBoosts", value);
            }
        }

        public bool ManageEnergy
        {
            get => _manageEnergy;
            set
            {
                if (value == _manageEnergy) return; SetPersistent("_manageEnergy", value);
            }
        }

        public bool ManageMagic
        {
            get => _manageMagic;
            set
            {
                if (value == _manageMagic) return; SetPersistent("_manageMagic", value);
            }
        }

        public bool FastCombat
        {
            get => _fastCombat;
            set
            {
                if (value == _fastCombat) return;
                SetPersistent("_fastCombat", value);
            }
        }

        public bool ManageGear
        {
            get => _manageGear;
            set
            {
                if (value == _manageGear) return; SetPersistent("_manageGear", value);
            }
        }

        public int[] TitanLoadout
        {
            get => _titanLoadout;
            set
            {
                SetPersistent("_titanLoadout", value);
            }
        }

        public int[] YggdrasilLoadout
        {
            get => _yggdrasilLoadout;
            set
            {
                SetPersistent("_yggdrasilLoadout", value);
            }
        }

        public bool ManageYggdrasil
        {
            get => _manageYggdrasil;
            set
            {
                if (value == _manageYggdrasil) return;
                SetPersistent("_manageYggdrasil", value);
            }
        }

        public bool ManageDiggers
        {
            get => _manageDiggers;
            set
            {
                if (value == _manageDiggers) return;
                SetPersistent("_manageDiggers", value);
            }
        }

        public bool UpgradeDiggers
        {
            get => _upgradeDiggers;
            set
            {
                if (value == _upgradeDiggers) return;
                SetPersistent("_upgradeDiggers", value);
            }
        }

        public bool ManageInventory
        {
            get => _manageInventory;
            set
            {
                if (value == _manageInventory) return;
                SetPersistent("_manageInventory", value);
            }
        }

        public bool AutoFight
        {
            get => _autoFight;
            set
            {
                if (value == _autoFight) return;
                SetPersistent("_autoFight", value);
            }
        }

        public bool AutoQuest
        {
            get => _autoQuest;
            set
            {
                if (value == _autoQuest) return;
                SetPersistent("_autoQuest", value);
            }
        }

        public bool AllowMajorQuests
        {
            get => _allowMajorQuests;
            set
            {
                if (value == _allowMajorQuests) return;
                SetPersistent("_allowMajorQuests", value);
            }
        }

        public bool AutoConvertBoosts
        {
            get => _autoConvertBoosts;
            set
            {
                if (value == _autoConvertBoosts) return;
                SetPersistent("_autoConvertBoosts", value);
            }
        }

        public bool AutoQuestITOPOD
        {
            get => _autoQuestItopod;
            set
            {
                if (value == _autoQuestItopod) return;
                SetPersistent("_autoQuestItopod", value);
            }
        }

        public int[] GoldDropLoadout
        {
            get => _goldDropLoadout;
            set
            {
                SetPersistent("_goldDropLoadout", value);
            }
        }

        public int[] MoneyPitLoadout
        {
            get => _moneyPitLoadout;
            set
            {
                SetPersistent("_moneyPitLoadout", value);
            }
        }

        public bool AutoMoneyPit
        {
            get => _autoMoneyPit;
            set
            {
                if (value == _autoMoneyPit) return;
                SetPersistent("_autoMoneyPit", value);
            }
        }

        public bool AutoSpin
        {
            get => _autoSpin;
            set
            {
                if (value == _autoSpin) return;
                SetPersistent("_autoSpin", value);
            }
        }

        public bool AutoRebirth
        {
            get => _autoRebirth;
            set
            {
                if (value == _autoRebirth) return;
                if (_savePath != null && !_disableSave)
                {
                    var patch = new JSONObject();
                    patch["_autoRebirth"] = value;
                    ApplyPatch(patch);
                }
                else
                {
                    _autoRebirth = value;
                    NotifyCustomPlanRunChanged();
                }
            }
        }

        public bool ManageWandoos
        {
            get => _manageWandoos;
            set
            {
                if (value == _manageWandoos) return;
                SetPersistent("_manageWandoos", value);
            }
        }

        public double MoneyPitThreshold
        {
            get => _moneyPitThreshold;
            set
            {
                SetPersistent("_moneyPitThreshold", value);
            }
        }

        public bool DoGoldSwap
        {
            get => _doGoldSwap;
            set
            {
                if (value == _doGoldSwap) return;
                SetPersistent("_doGoldSwap", value);
            }
        }
        
        public bool SnipeBossOnly
        {
            get => _snipeBossOnly;
            set
            {
                if (value == _snipeBossOnly) return;
                SetPersistent("_snipeBossOnly", value);
            }
        }

        public int[] BoostBlacklist
        {
            get => _boostBlacklist;
            set
            {
                SetPersistent("_boostBlacklist", value);
            }
        }

        public bool RecoverHealth
        {
            get => _recoverHealth;
            set
            {
                if (value == _recoverHealth) return;
                SetPersistent("_recoverHealth", value);
            }
        }

        public int CombatMode
        {
            get => _combatMode;
            set
            {
                if (value == _combatMode) return;
                SetPersistent("_combatMode", value);
            }
        }

        public bool AllowZoneFallback
        {
            get => _allowZoneFallback;
            set
            {
                if (value == _allowZoneFallback) return;
                SetPersistent("_allowZoneFallback", value);
            }
        }

        public bool AbandonMinors
        {
            get => _abandonMinors;
            set
            {
                if (value == _abandonMinors) return;
                SetPersistent("_abandonMinors", value);
            }
        }

        public int MinorAbandonThreshold
        {
            get => _minorAbandonThreshold;
            set
            {
                if (value == _minorAbandonThreshold) return;
                SetPersistent("_minorAbandonThreshold", value);
            }
        }

        public int QuestCombatMode
        {
            get => _questCombatMode;
            set
            {
                if (value == _questCombatMode) return;
                SetPersistent("_questCombatMode", value);
            }
        }

        public bool QuestFastCombat
        {
            get => _questFastCombat;
            set
            {
                if (value == _questFastCombat) return;
                SetPersistent("_questFastCombat", value);
            }
        }

        public bool AutoSpellSwap
        {
            get => _autoSpellSwap;
            set
            {
                if (value == _autoSpellSwap) return;
                SetPersistent("_autoSpellSwap", value);
            }
        }

        public int SpaghettiThreshold
        {
            get => _spaghettiThreshold;
            set
            {
                if (value == _spaghettiThreshold) return;
                SetPersistent("_spaghettiThreshold", value);
            }
        }

        public bool CastBloodSpells
        {
            get => _castBloodSpells;
            set
            {
                if (value == _castBloodSpells) return;
                SetPersistent("_castBloodSpells", value);
            }
        }

        public int IronPillThreshold
        {
            get => _ironPillThreshold;
            set
            {
                if (value == _ironPillThreshold) return;
                SetPersistent("_ironPillThreshold", value);
            }
        }

        public int BloodMacGuffinAThreshold
        {
            get => _bloodMacGuffinAThreshold;
            set
            {
                if (value == _bloodMacGuffinAThreshold) return;
                SetPersistent("_bloodMacGuffinAThreshold", value);
            }
        }

        public int BloodMacGuffinBThreshold
        {
            get => _bloodMacGuffinBThreshold;
            set
            {
                if (value == _bloodMacGuffinBThreshold) return;
                SetPersistent("_bloodMacGuffinBThreshold", value);
            }
        }

        public int CounterfeitThreshold
        {
            get => _counterfeitThreshold;
            set
            {
                if (value == _counterfeitThreshold) return;
                SetPersistent("_counterfeitThreshold", value);
            }
        }

        public bool AutoBuyEM
        {
            get => _autoBuyEm;
            set
            {
                if (value == _autoBuyEm) return;
                SetPersistent("_autoBuyEm", value);
            }
        }

        public double BloodNumberThreshold
        {
            get => _bloodNumberThreshold;
            set
            {
                if (value == _bloodNumberThreshold) return;
                SetPersistent("_bloodNumberThreshold", value);
            }
        }

        public int[] QuickLoadout
        {
            get => _quickLoadout;
            set => _quickLoadout = value;
        }

        public int[] QuickDiggers
        {
            get => _quickDiggers;
            set => _quickDiggers = value;
        }

        public bool GlobalEnabled
        {
            get => _globalEnabled;
            set
            {
                if (value == _globalEnabled) return;
                if (_savePath != null && !_disableSave)
                {
                    var patch = new JSONObject();
                    patch["_globalEnabled"] = value;
                    ApplyPatch(patch);
                }
                else _globalEnabled = value;
            }
        }

        public bool CombatEnabled
        {
            get => _combatEnabled;
            set
            {
                if (value == _combatEnabled) return;
                if (_savePath != null && !_disableSave)
                {
                    var patch = new JSONObject();
                    patch["_combatEnabled"] = value;
                    ApplyPatch(patch);
                }
                else _combatEnabled = value;
            }
        }

        public bool ManualMinors
        {
            get => _manualMinors;
            set
            {
                if (value == _manualMinors) return;
                SetPersistent("_manualMinors", value);
            }
        }

        public bool UseButterMajor
        {
            get => _useButterMajor;
            set
            {
                if (value == _useButterMajor) return;
                SetPersistent("_useButterMajor", value);
            }
        }

        public bool ManageR3
        {
            get => _manageR3;
            set
            {
                if (value == _manageR3) return;
                SetPersistent("_manageR3", value);
            }
        }

        public bool UseButterMinor
        {
            get => _useButterMinor;
            set
            {
                if (value == _useButterMinor) return;
                SetPersistent("_useButterMinor", value);
            }
        }

        public bool ActivateFruits
        {
            get => _activateFruits;
            set
            {
                if (value == _activateFruits) return;
                SetPersistent("_activateFruits", value);
            }
        }
        public int[] WishPriorities
        {
            get => _wishPriorities;
            set
            {
                if (value == _wishPriorities) return;
                SetPersistent("_wishPriorities", value);
            }
        }

        public bool WishSortPriorities
        {
            get => _wishSortPriorities;
            set
            {
                if (value == _wishSortPriorities) return;
                SetPersistent("_wishSortPriorities", value);
            }
        }

        public bool WishSortOrder
        {
            get => _wishSortOrder;
            set
            {
                if (value == _wishSortOrder) return;
                SetPersistent("_wishSortOrder", value);
            }
        }

        public bool BeastMode
        {
            get => _beastMode;
            set
            {
                if (value == _beastMode) return;
                SetPersistent("_beastMode", value);
            }
        }

        public int CubePriority
        {
            get => _cubePriority;
            set
            {
                if (value == _cubePriority) return;
                SetPersistent("_cubePriority", value);
            }
        }

        public bool ManageNGUDiff
        {
            get => _manageNguDiff;
            set
            {
                if (value == _manageNguDiff) return;
                SetPersistent("_manageNguDiff", value);
            }
        }

        public int[] EnergyNguWeights
        {
            get => (int[]) _energyNguWeights.Clone();
            set
            {
                var patch = new JSONObject();
                patch["_energyNguWeights"] = CustomPlans.CustomPlanStore.SerializeNguWeights(value, 9, "_energyNguWeights");
                if (_savePath != null && !_disableSave) ApplyPatch(patch);
                else _energyNguWeights = (int[]) value.Clone();
            }
        }

        public int[] MagicNguWeights
        {
            get => (int[]) _magicNguWeights.Clone();
            set
            {
                var patch = new JSONObject();
                patch["_magicNguWeights"] = CustomPlans.CustomPlanStore.SerializeNguWeights(value, 7, "_magicNguWeights");
                if (_savePath != null && !_disableSave) ApplyPatch(patch);
                else _magicNguWeights = (int[]) value.Clone();
            }
        }

        public string AllocationFile
        {
            get => _allocationFile;
            set
            {
                if (value == _allocationFile) return;
                SetPersistent("_allocationFile", value);
            }
        }

        public bool ManageGoldLoadouts
        {
            get => _manageGoldLoadouts;
            set
            {
                if (value == _manageGoldLoadouts) return;
                SetPersistent("_manageGoldLoadouts", value);
            }
        }

        public int ResnipeTime
        {
            get => _resnipeTime;
            set
            {
                if (value == _resnipeTime) return;
                SetPersistent("_resnipeTime", value);
            }
        }

        public bool[] TitanMoneyDone
        {
            get => _titanMoneyDone;
            set
            {
                if (_titanMoneyDone != null && _titanMoneyDone.SequenceEqual(value)) return;
                SetPersistent("_titanMoneyDone", value);
            }
        }

        public bool[] TitanGoldTargets
        {
            get => _titanGoldTargets;
            set
            {
                if (_titanGoldTargets != null && _titanGoldTargets.SequenceEqual(value)) return;
                SetPersistent("_titanGoldTargets", value);
            }
        }

        public bool[] TitanSwapTargets
        {
            get => _titanSwapTargets;
            set
            {
                if (_titanSwapTargets != null && _titanSwapTargets.SequenceEqual(value)) return;
                SetPersistent("_titanSwapTargets", value);
            }
        }

        public bool GoldCBlockMode
        {
            get => _goldCBlockMode;
            set
            {
                if (value == _goldCBlockMode) return;
                SetPersistent("_goldCBlockMode", value);
            }
        }

        public bool DebugAllocation
        {
            get => _debugAllocation;
            set => _debugAllocation = value;
        }

        public bool OptimizeITOPODFloor
        {
            get => _optimizeItopodFloor;
            set
            {
                if (value == _optimizeItopodFloor) return;
                SetPersistent("_optimizeItopodFloor", value);
            }
        }

        public bool AdventureTargetITOPOD
        {
            get => _adventureTargetItopod;
            set
            {
                if (value == _adventureTargetItopod) return;
                SetPersistent("_adventureTargetItopod", value);
            }
        }

        public int ITOPODCombatMode
        {
            get => _itopodCombatMode;
            set
            {
                if (value == _itopodCombatMode) return;
                SetPersistent("_itopodCombatMode", value);
            }
        }

        public bool ITOPODBeastMode
        {
            get => _itopodBeastMode;
            set
            {
                if (value == _itopodBeastMode) return;
                SetPersistent("_itopodBeastMode", value);
            }
        }

        public bool ITOPODFastCombat
        {
            get => _itopodFastCombat;
            set
            {
                if (value == _itopodFastCombat) return;
                SetPersistent("_itopodFastCombat", value);
            }
        }

        public bool ITOPODRecoverHP
        {
            get => _itopodRecoverHp;
            set
            {
                if (value == _itopodRecoverHp) return;
                SetPersistent("_itopodRecoverHp", value);
            }
        }

        public bool ITOPODPrecastBuffs
        {
            get => _itopodPrecastBuffs;
            set
            {
                if (value == _itopodPrecastBuffs) return;
                SetPersistent("_itopodPrecastBuffs", value);
            }
        }

        public bool DisableOverlay
        {
            get => _disableOverlay;
            set
            {
                if (value == _disableOverlay) return;
                SetPersistent("_disableOverlay", value);
            }
        }

        public int YggSwapThreshold
        {
            get => _yggSwapThreshold;
            set
            {
                if (value == _yggSwapThreshold) return;
                SetPersistent("_yggSwapThreshold", value);
            }
        }

        public bool MoreBlockParry
        {
            get => _moreBlockParry;
            set
            {
                if (value == _moreBlockParry) return;
                SetPersistent("_moreBlockParry", value);
            }
        }

        public int[] SpecialBoostBlacklist
        {
            get => _specialBoostBlacklist;
            set
            {
                if (_specialBoostBlacklist != null && _specialBoostBlacklist.SequenceEqual(value)) return;
                SetPersistent("_specialBoostBlacklist", value);
            }
        }

        public int[] BlacklistedBosses
        {
            get => _blacklistedBosses;
            set
            {
                if (_blacklistedBosses != null && _blacklistedBosses.SequenceEqual(value)) return;
                SetPersistent("_blacklistedBosses", value);
            }
        }

        internal bool NeedsGoldSwap()
        {
            for (var i = 0; i < TitanGoldTargets.Length; i++)
            {
                if (!TitanGoldTargets[i])
                    continue;

                if (TitanGoldTargets[i] && !TitanMoneyDone[i])
                    return true;
            }

            return false;
        }

        public bool HackAdvance
        {
            get => _hackAdvance;
            set
            {
                if (value == _hackAdvance) return;
                SetPersistent("_hackAdvance", value);
            }
        }

        public bool UseCustomRebirthPlan
        {
            get => _useCustomRebirthPlan;
            set
            {
                if (value == _useCustomRebirthPlan) return;
                if (_savePath != null && !_disableSave)
                {
                    var patch = new JSONObject();
                    patch["_useCustomRebirthPlan"] = value;
                    ApplyPatch(patch);
                }
                else
                {
                    _useCustomRebirthPlan = value;
                    NotifyCustomPlanRunChanged();
                }
            }
        }

        public string CustomPlanFile
        {
            get => _customPlanFile;
            set
            {
                if (value == _customPlanFile) return;
                if (_savePath != null && !_disableSave)
                {
                    var patch = new JSONObject();
                    patch["_customPlanFile"] = value;
                    ApplyPatch(patch);
                }
                else
                {
                    _customPlanFile = value;
                    NotifyCustomPlanRunChanged();
                }
            }
        }

        public bool CustomAdventureEnabled
        {
            get => _customAdventureEnabled;
            set
            {
                if (value == _customAdventureEnabled) return;
                SetPersistent("_customAdventureEnabled", value);
            }
        }

        public AdventureRule[] CustomAdventureRules => _customAdventureRules ?? new AdventureRule[0];

        public int CustomAdventureMode
        {
            get => _customAdventureMode;
            set
            {
                if (value == _customAdventureMode) return;
                if (value < 0 || value > 3) throw new ArgumentOutOfRangeException(nameof(value));
                SetPersistent("_customAdventureMode", value);
            }
        }

        public int CustomAdventureMaxItopodDeaths
        {
            get => _customAdventureMaxItopodDeaths < 1 ? 3 : Math.Min(1000, _customAdventureMaxItopodDeaths);
            set
            {
                if (value < 1 || value > 1000) throw new ArgumentOutOfRangeException(nameof(value));
                if (value == _customAdventureMaxItopodDeaths) return;
                SetPersistent("_customAdventureMaxItopodDeaths", value);
            }
        }

        public int CustomAdventureZone
        {
            get => _customAdventureZone;
            set
            {
                if (value == _customAdventureZone) return;
                SetPersistent("_customAdventureZone", value);
            }
        }

        public bool CustomAdventureGoldPush
        {
            get => _customAdventureGoldPush;
            set
            {
                if (value == _customAdventureGoldPush) return;
                SetPersistent("_customAdventureGoldPush", value);
            }
        }

        public bool CustomAutoBoostEnabled
        {
            get => _customAutoBoostEnabled;
            set
            {
                if (value == _customAutoBoostEnabled) return;
                SetPersistent("_customAutoBoostEnabled", value);
            }
        }

        public bool CustomAutoExpEnabled => _customAutoExpEnabled;
        public int CustomExpEnergyRatio => ExpRatio(_customExpEnergyRatio, 5);
        public int CustomExpMagicRatio => ExpRatio(_customExpMagicRatio, 1);
        public int CustomExpEnergyPowerRatio => ExpRatio(_customExpEnergyPowerRatio, 5);
        public int CustomExpEnergyCapRatio => ExpRatio(_customExpEnergyCapRatio, 160000);
        public int CustomExpEnergyBarsRatio => ExpRatio(_customExpEnergyBarsRatio, 4);
        public int CustomExpMagicPowerRatio => ExpRatio(_customExpMagicPowerRatio, 5);
        public int CustomExpMagicCapRatio => ExpRatio(_customExpMagicCapRatio, 160000);
        public int CustomExpMagicBarsRatio => ExpRatio(_customExpMagicBarsRatio, 4);

        private static int ExpRatio(int value, int fallback) => value < 1 ? fallback : Math.Min(1000000000, value);

        public bool CustomAutoMoneyPitEnabled
        {
            get => _customAutoMoneyPitEnabled;
            set
            {
                if (value == _customAutoMoneyPitEnabled) return;
                SetPersistent("_customAutoMoneyPitEnabled", value);
            }
        }

        public int CustomAutoBoostCubePowerRatio
        {
            get => _customAutoBoostCubePowerRatio < 1 ? 2 : Math.Min(4, _customAutoBoostCubePowerRatio);
            set
            {
                var normalized = Math.Max(1, Math.Min(4, value));
                if (normalized == _customAutoBoostCubePowerRatio) return;
                SetPersistent("_customAutoBoostCubePowerRatio", normalized);
            }
        }

        public int CustomAugmentsBloodEstimateSeconds
        {
            get => _customAugmentsBloodEstimateSeconds < 1 ? 300 : Math.Min(100000, _customAugmentsBloodEstimateSeconds);
            set
            {
                var normalized = Math.Max(1, Math.Min(100000, value));
                if (normalized == _customAugmentsBloodEstimateSeconds) return;
                SetPersistent("_customAugmentsBloodEstimateSeconds", normalized);
            }
        }

        public bool CustomAutoBloodSpellsEnabled
        {
            get => _customAutoBloodSpellsEnabled;
            set
            {
                if (value == _customAutoBloodSpellsEnabled) return;
                SetPersistent("_customAutoBloodSpellsEnabled", value);
            }
        }

        public int CustomBloodSpaghettiGoalPercent
        {
            get => Math.Max(0, Math.Min(100000, _customBloodSpaghettiGoalPercent));
            set
            {
                var normalized = Math.Max(0, Math.Min(100000, value));
                if (normalized == _customBloodSpaghettiGoalPercent) return;
                SetPersistent("_customBloodSpaghettiGoalPercent", normalized);
            }
        }

        public int CustomBloodCounterfeitGoalPercent
        {
            get => Math.Max(0, Math.Min(100000, _customBloodCounterfeitGoalPercent));
            set
            {
                var normalized = Math.Max(0, Math.Min(100000, value));
                if (normalized == _customBloodCounterfeitGoalPercent) return;
                SetPersistent("_customBloodCounterfeitGoalPercent", normalized);
            }
        }

        public int CustomIronPillSaveSeconds
        {
            get => Math.Max(0, Math.Min(604800, _customIronPillSaveSeconds));
            set
            {
                var normalized = Math.Max(0, Math.Min(604800, value));
                if (normalized == _customIronPillSaveSeconds) return;
                SetPersistent("_customIronPillSaveSeconds", normalized);
            }
        }

        public double CustomIronPillMinWorth
        {
            get => double.IsNaN(_customIronPillMinWorth) || double.IsInfinity(_customIronPillMinWorth)
                ? 0d : Math.Max(0d, Math.Min(1e8, _customIronPillMinWorth));
            set
            {
                if (double.IsNaN(value) || double.IsInfinity(value) || value < 0d || value > 1e8)
                    throw new ArgumentOutOfRangeException(nameof(value));
                if (value == _customIronPillMinWorth) return;
                SetPersistent("_customIronPillMinWorth", value);
            }
        }

        public IronPillHistoryEntry[] CustomIronPillHistory => _customIronPillHistory ?? new IronPillHistoryEntry[0];

        internal void AddIronPillHistory(IronPillHistoryEntry entry)
        {
            if (!IsValidIronPillHistoryEntry(entry)) throw new ArgumentOutOfRangeException(nameof(entry));
            var history = new[] {entry}.Concat(CustomIronPillHistory.Where(IsValidIronPillHistoryEntry)).Take(10).ToArray();
            if (_savePath == null || _disableSave) { _customIronPillHistory = history; return; }
            lock (CustomPlans.PresetLibraryStore.SyncRoot)
            {
                var candidate = CopyPersistent();
                candidate._customIronPillHistory = history;
                Commit(candidate, false);
            }
        }

        public bool ManualAdventureEnabled
        {
            get => _manualAdventureEnabled;
            set
            {
                if (value == _manualAdventureEnabled) return;
                SetPersistent("_manualAdventureEnabled", value);
            }
        }

        public double[] ManualAdventureGearWeights
        {
            get => (double[]) (_manualAdventureGearWeights ?? new double[5]).Clone();
            set
            {
                var patch = new JSONObject();
                patch["_manualAdventureGearWeights"] = SerializeManualAdventureWeights(value, "_manualAdventureGearWeights", 5);
                if (_savePath != null && !_disableSave) ApplyPatch(patch);
                else _manualAdventureGearWeights = (double[]) value.Clone();
            }
        }

        public double[] ManualAdventureBeardWeights
        {
            get => (double[]) (_manualAdventureBeardWeights ?? new double[7]).Clone();
            set
            {
                var patch = new JSONObject();
                patch["_manualAdventureBeardWeights"] = SerializeManualAdventureWeights(value, "_manualAdventureBeardWeights", 7);
                if (_savePath != null && !_disableSave) ApplyPatch(patch);
                else _manualAdventureBeardWeights = (double[]) value.Clone();
            }
        }

        public double[] ManualAdventureDiggerWeights
        {
            get => (double[]) (_manualAdventureDiggerWeights ?? new double[12]).Clone();
            set
            {
                var patch = new JSONObject();
                patch["_manualAdventureDiggerWeights"] = SerializeManualAdventureWeights(value, "_manualAdventureDiggerWeights", 12);
                if (_savePath != null && !_disableSave) ApplyPatch(patch);
                else _manualAdventureDiggerWeights = (double[]) value.Clone();
            }
        }

        public int ManualAdventureZone
        {
            get => _manualAdventureZone;
            set
            {
                if (value == _manualAdventureZone) return;
                SetPersistent("_manualAdventureZone", value);
            }
        }

        public int ManualAdventureLoadout
        {
            get => _manualAdventureLoadout < -1 || _manualAdventureLoadout > 2 ? -1 : _manualAdventureLoadout;
            set
            {
                if (value < -1 || value > 2) throw new ArgumentOutOfRangeException(nameof(value));
                if (value == _manualAdventureLoadout) return;
                SetPersistent("_manualAdventureLoadout", value);
            }
        }

        public bool ManualTitanEnabled
        {
            get => _manualTitanEnabled;
            set
            {
                if (value == _manualTitanEnabled) return;
                SetPersistent("_manualTitanEnabled", value);
            }
        }

        public int ManualTitanZone
        {
            get => Managers.ZoneHelpers.TitanZones.Contains(_manualTitanZone) ? _manualTitanZone : 16;
            set
            {
                if (!Managers.ZoneHelpers.TitanZones.Contains(value)) throw new ArgumentOutOfRangeException(nameof(value));
                if (value == _manualTitanZone) return;
                SetPersistent("_manualTitanZone", value);
            }
        }

        public bool ManualTitanRequireAT
        {
            get => _manualTitanRequireAT;
            set
            {
                if (value == _manualTitanRequireAT) return;
                SetPersistent("_manualTitanRequireAT", value);
            }
        }

        public int ManualTitanLoadout
        {
            get => _manualTitanLoadout < -1 || _manualTitanLoadout > 2 ? 1 : _manualTitanLoadout;
            set
            {
                if (value < -1 || value > 2) throw new ArgumentOutOfRangeException(nameof(value));
                if (value == _manualTitanLoadout) return;
                SetPersistent("_manualTitanLoadout", value);
            }
        }

        public int ManualTitanMaxDeaths
        {
            get => _manualTitanMaxDeaths < 1 ? 3 : Math.Min(1000, _manualTitanMaxDeaths);
            set
            {
                if (value < 1 || value > 1000) throw new ArgumentOutOfRangeException(nameof(value));
                if (value == _manualTitanMaxDeaths) return;
                SetPersistent("_manualTitanMaxDeaths", value);
            }
        }

        public bool ManualAdventureBossOnly
        {
            get => _manualAdventureBossOnly;
            set
            {
                if (value == _manualAdventureBossOnly) return;
                SetPersistent("_manualAdventureBossOnly", value);
            }
        }

        public bool ManualAdventureRecoverHealth
        {
            get => _manualAdventureRecoverHealth;
            set
            {
                if (value == _manualAdventureRecoverHealth) return;
                SetPersistent("_manualAdventureRecoverHealth", value);
            }
        }

        public bool ManualAdventurePrecastBuffs
        {
            get => _manualAdventurePrecastBuffs;
            set
            {
                if (value == _manualAdventurePrecastBuffs) return;
                SetPersistent("_manualAdventurePrecastBuffs", value);
            }
        }

        public bool ManualAdventureFastCombat
        {
            get => _manualAdventureFastCombat;
            set
            {
                if (value == _manualAdventureFastCombat) return;
                SetPersistent("_manualAdventureFastCombat", value);
            }
        }

        public bool ManualAdventureBeastMode
        {
            get => _manualAdventureBeastMode;
            set
            {
                if (value == _manualAdventureBeastMode) return;
                SetPersistent("_manualAdventureBeastMode", value);
            }
        }

        public bool ManualAdventureAllowFallback
        {
            get => _manualAdventureAllowFallback;
            set
            {
                if (value == _manualAdventureAllowFallback) return;
                SetPersistent("_manualAdventureAllowFallback", value);
            }
        }
    }
}
