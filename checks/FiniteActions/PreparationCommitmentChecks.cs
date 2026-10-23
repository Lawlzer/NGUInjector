using System;
using System.IO;
using NGUInjector;
using SimpleJSON;

internal static class PreparationCommitmentChecks
{
    internal static int Run(string root)
    {
        var count = 0;
        void Equal<T>(T expected, T actual, string message)
        {
            count++;
            if (!Equals(expected, actual)) throw new InvalidOperationException(message + ": expected " + expected + ", actual " + actual);
        }
        var original = new JSONObject(); original["plan"] = "daily"; original["entry"] = "";
        original["started"] = 500d; original["fightWindowSeconds"] = 3d; original["manualRebirth"] = false;
        original["opportunity"] = "explicit-request-1"; original["consumePowerFruits"] = true;
        var fruits = new JSONObject(); fruits["1"] = 4; fruits["6"] = 2; original["fruitHarvests"] = fruits;
        var id = new FiniteActionIdentity("fight-preparation", "fight-preparation", "rebirth:7", "rebirth:7", "explicit-request-1");
        var record = new FiniteActionRecord { Identity = id, State = FiniteActionState.Prepared, Before = original.ToString(), After = "acquired" };
        var ledger = new FiniteActionLedger(Path.Combine(root, "preparation-ledger")); ledger.Save(record);
        Equal(true, NGUInjector.Main.PreparationAcquisitionCanRetry(record), "pre-model acquisition failure keeps same retryable process");
        var imported = new FiniteActionLedger(Path.Combine(root, "preparation-ledger")).Find(id);
        Equal(true, NGUInjector.Main.PreparationAcquisitionCanRetry(imported), "durable pre-model failure remains retryable after reload");
        Equal(id.Key, imported.Identity.Key, "retry/import does not invent another process/acquisition");
        Equal(record.Before, imported.Before, "retry/import preserves exact entry/start/window/fruit identities");
        Equal("", JsonFiles.ParseObject(imported.Before)["entry"].Value, "manual preparation needs no invented scheduler entry");
        var alpha = NGUInjector.Main.PreparationFruitOpportunity(id, 1);
        Equal(alpha, NGUInjector.Main.PreparationFruitOpportunity(imported.Identity, 1), "fruit step opportunity survives process import");
        Equal(false, alpha == NGUInjector.Main.PreparationFruitOpportunity(imported.Identity, 6), "each original preparation fruit has its own stable opportunity");
        Equal(4, JsonFiles.ParseObject(imported.Before)["fruitHarvests"]["1"].AsInt, "later native harvest count cannot replace original alpha identity");
        record.After = "selected";
        Equal(false, NGUInjector.Main.PreparationAcquisitionCanRetry(record), "selected exact setup cannot recover by replacement search");
        record.After = "acquired"; record.State = FiniteActionState.Unknown;
        Equal(false, NGUInjector.Main.PreparationAcquisitionCanRetry(record), "unknown preparation never returns to uninvoked acquisition");
        record.State = FiniteActionState.Committed;
        Equal(false, NGUInjector.Main.PreparationAcquisitionCanRetry(record), "completed preparation cannot acquire another entry fight");
        Equal(false, NGUInjector.Main.PreparationContinuesRebirth(record), "committed explicit fight-only prep does not authorize automatic rebirth");
        original["manualRebirth"] = true; record.Before = original.ToString();
        Equal(true, NGUInjector.Main.PreparationContinuesRebirth(record), "committed manual rebirth prep preserves pending rebirth intent");
        Equal("explicit-request-1", JsonFiles.ParseObject(record.Before)["opportunity"].Value, "manual completion retains original explicit request opportunity");
        original["manualRebirth"] = false; original["opportunity"] = "automatic:daily"; record.Before = original.ToString();
        record.Identity = new FiniteActionIdentity("fight-preparation", "fight-preparation", "rebirth:7", "rebirth:7", "automatic:daily");
        Equal(true, NGUInjector.Main.PreparationContinuesRebirth(record), "automatic preparation retains its original automatic completion intent");
        var rebirth = new FiniteActionRecord { Identity = new FiniteActionIdentity("rebirth", "rebirth", "rebirth:7", "rebirth:7", "engage"),
            State = FiniteActionState.Committed, Dirty = true };
        Equal(false, NGUInjector.Main.KnownRebirthResultCanFinish(rebirth, 7, 8), "known native rebirth cannot finish dependent checkpoint while result is dirty");
        rebirth.Dirty = false;
        Equal(true, NGUInjector.Main.KnownRebirthResultCanFinish(rebirth, 7, 8), "stored original rebirth result finishes its original request without another invocation");
        Equal(false, NGUInjector.Main.KnownRebirthResultCanFinish(rebirth, 7, 7), "historical committed record cannot pretend a rolled-back native branch rebirthed");
        Equal(false, NGUInjector.Main.KnownRebirthResultCanFinish(rebirth, 7, 9), "unrelated later epoch cannot be adopted as the original result");
        rebirth.State = FiniteActionState.Unknown;
        Equal(false, NGUInjector.Main.KnownRebirthResultCanFinish(rebirth, 7, 8), "matching counter never converts imported unknown into committed result");
        rebirth.State = FiniteActionState.Committed;
        rebirth.Identity = new FiniteActionIdentity("rebirth", "rebirth", "rebirth:8", "rebirth:8", "engage");
        Equal(false, NGUInjector.Main.KnownRebirthResultCanFinish(rebirth, 7, 8), "a newer acquisition cannot replace the original manual rebirth result");
        return count;
    }
}
