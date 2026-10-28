using System;
using System.Globalization;
using System.IO;
using NGUInjector.CustomPlans;
using NGUInjector.Managers;
using SimpleJSON;

namespace NGUInjector
{
    internal partial class Main
    {
        private sealed class IronBloodWindow
        {
            internal string Owner, Phase;
            internal object Model, Blood;
            internal long Rebirth;
            internal double Origin, Started, Deadline, MinWorth, RetryNotBefore;
            internal int SaveSeconds;
            internal bool Dirty;
        }
        private IronBloodWindow _ironWindow;
        private bool _ironWindowLoaded;
        private string IronBloodWindowPath => Path.Combine(_dir, "iron-pill-window-v1.json");
        private double IronBloodOrigin => Character.totalPlaytime.totalseconds - Character.rebirthTime.totalseconds;

        private void LoadIronBloodWindow()
        {
            if (_ironWindowLoaded) return;
            _ironWindowLoaded = true;
            if (string.IsNullOrEmpty(_dir)) return;
            try
            {
                if (!File.Exists(IronBloodWindowPath))
                {
                    if (Directory.Exists(Path.Combine(_dir, "finite-actions-v1")))
                    {
                        // Retired files are inert. Conservatively cancel any old saving
                        // window once rather than infer whether its entry flush ran.
                        _ironWindow = NewIronBloodWindow(null);
                        FinishIronBloodWindow(900d);
                    }
                    return;
                }
                if (new FileInfo(IronBloodWindowPath).Length > 4096) throw new FormatException("Iron Pill checkpoint is too large.");
                var data = JsonFiles.ParseObject(File.ReadAllText(IronBloodWindowPath));
                var rebirth = 0L;
                if (!data["version"].IsNumber || data["version"].AsDouble != 1d || !data["owner"].IsString
                    || string.IsNullOrEmpty(data["owner"].Value) || !data["phase"].IsString
                    || !data["rebirth"].IsString || !long.TryParse(data["rebirth"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out rebirth))
                    throw new FormatException("Invalid Iron Pill checkpoint identity.");
                foreach (var key in new[] { "origin", "started", "deadline", "saveSeconds", "minWorth", "retryNotBefore" })
                    if (!data[key].IsNumber || double.IsNaN(data[key].AsDouble) || double.IsInfinity(data[key].AsDouble))
                        throw new FormatException("Invalid Iron Pill checkpoint number: " + key);
                var window = new IronBloodWindow { Model = Character, Blood = Character.bloodMagic, Owner = data["owner"].Value, Phase = data["phase"].Value, Rebirth = rebirth,
                    Origin = data["origin"].AsDouble, Started = data["started"].AsDouble, Deadline = data["deadline"].AsDouble,
                    SaveSeconds = data["saveSeconds"].AsInt, MinWorth = data["minWorth"].AsDouble, RetryNotBefore = data["retryNotBefore"].AsDouble };
                if (window.Started < 0d || window.SaveSeconds < 0 || window.SaveSeconds > 604800
                    || data["saveSeconds"].AsDouble != window.SaveSeconds || window.MinWorth < 0d || window.MinWorth > 1e8d
                    || Math.Abs(window.Deadline - window.Started - IronPillAutomation.PrepLimitSeconds(window.SaveSeconds)) > .001d
                    || window.RetryNotBefore < 0d || window.RetryNotBefore > Character.rebirthTime.totalseconds + 900.001d
                    || window.Phase != "closed" && window.RetryNotBefore != 0d
                    || (window.Phase != "starting" && window.Phase != "saving" && window.Phase != "closed"))
                    throw new FormatException("Invalid Iron Pill checkpoint window.");
                _ironWindow = window;
                var now = Character.rebirthTime.totalseconds;
                if (!IronBloodWindowCurrent(window) || now < window.Started)
                {
                    _ironWindow = NewIronBloodWindow(null);
                    FinishIronBloodWindow(window.Phase == "closed" ? 0d : 10d);
                    return;
                }
                _customIronPillPlanName = window.Owner;
                _customIronPillRetryAt = window.RetryNotBefore;
                if (window.Phase == "starting")
                {
                    // A restart cannot determine whether the entry flush was entered.
                    FinishIronBloodWindow(900d);
                    return;
                }
                if (window.Phase == "saving")
                {
                    if (now >= window.Deadline) FinishIronBloodWindow(900d);
                    else _customIronPillPrepStartedAt = window.Started;
                }
            }
            catch (Exception error)
            {
                // Corruption is a local cancelled window, not a startup/dependency block.
                _ironWindow = NewIronBloodWindow(null);
                FinishIronBloodWindow(900d);
                LogError("Reading Iron Pill checkpoint", error);
            }
        }

        private bool IronBloodWindowCurrent(IronBloodWindow window) => window != null
            && ReferenceEquals(window.Model, Character) && ReferenceEquals(window.Blood, Character.bloodMagic)
            && window.Owner == (Settings.CustomPlanFile ?? "default") && window.Rebirth == Character.stats.rebirthNumber
            && Math.Abs(window.Origin - IronBloodOrigin) <= 5d;

        private IronBloodWindow NewIronBloodWindow(CustomPlan plan)
        {
            var now = Character.rebirthTime.totalseconds;
            var seconds = EffectiveCustomIronPillSaveSeconds(plan);
            return new IronBloodWindow { Model = Character, Blood = Character.bloodMagic, Owner = Settings.CustomPlanFile ?? "default", Rebirth = Character.stats.rebirthNumber,
                Origin = IronBloodOrigin, Started = now, SaveSeconds = seconds, MinWorth = EffectiveCustomIronPillMinWorth(plan),
                Deadline = now + IronPillAutomation.PrepLimitSeconds(seconds), Phase = "starting", Dirty = true };
        }

        private bool SaveIronBloodWindow()
        {
            var window = _ironWindow;
            if (window == null || !window.Dirty) return true;
            try
            {
                if (string.IsNullOrEmpty(_dir)) throw new IOException("Iron Pill checkpoint directory is unavailable.");
                var data = new JSONObject(); data["version"] = 1;
                data["owner"] = window.Owner; data["rebirth"] = ActionNumber(window.Rebirth); data["origin"] = window.Origin;
                data["started"] = window.Started; data["deadline"] = window.Deadline; data["saveSeconds"] = window.SaveSeconds;
                data["minWorth"] = window.MinWorth; data["retryNotBefore"] = window.RetryNotBefore; data["phase"] = window.Phase;
                JsonFiles.WriteAtomic(IronBloodWindowPath, data.ToString());
                window.Dirty = false;
                return true;
            }
            catch (Exception error) { LogError("Saving Iron Pill checkpoint", error); return false; }
        }

        private bool AcquireIronBloodWindow(CustomPlan plan)
        {
            if (_ironWindow?.Phase == "saving") return true;
            if (_ironWindow?.Phase != "starting") _ironWindow = NewIronBloodWindow(plan);
            return SaveIronBloodWindow();
        }

        private void FinishIronBloodWindow(double retrySeconds)
        {
            // Retire in memory before storage, history, logging or UI can fail.
            _customIronPillPrepStartedAt = -1d;
            _customIronPillPrepLastWaitLogAt = -1d;
            _customIronPillRetryAt = (Character?.rebirthTime?.totalseconds ?? 0d) + retrySeconds;
            if (_ironWindow == null) return;
            _ironWindow.Phase = "closed";
            _ironWindow.RetryNotBefore = _customIronPillRetryAt;
            _ironWindow.Dirty = true;
            SaveIronBloodWindow();
        }
    }
}
