using System;
using System.Collections;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

public static partial class OwnershipChecks
{
    static Action DiggerRendered;
    static int DiggerRenders;
    public static void DiggerPresentation() { DiggerRenders++; DiggerRendered?.Invoke(); }
    public static string SupportInputRead(object input) => (string)input.GetType().GetField("m_Text", All).GetValue(input);
    public static void SupportInputWrite(object input, string text) { input.GetType().GetField("m_Text", All).SetValue(input, text); }
    static void ConfigureSupportText(ModuleDefinition module)
    {
        foreach (var instruction in module.Types.SelectMany(Types).SelectMany(x => x.Methods).Where(x => x.HasBody).SelectMany(x => x.Body.Instructions))
        {
            var call = instruction.Operand as MethodReference;
            if (call?.DeclaringType.FullName != "UnityEngine.UI.InputField" || call.Name != "get_text" && call.Name != "set_text") continue;
            instruction.OpCode = OpCodes.Call;
            instruction.Operand = module.ImportReference(typeof(OwnershipChecks).GetMethod(call.Name == "get_text" ? nameof(SupportInputRead) : nameof(SupportInputWrite), All));
        }
    }
    static void SetupNativeDiggers()
    {
        S(G(G(Character, "buttons"), "diggers"), "m_Interactable", true);
        var dc = G(Character, "allDiggers"); var diggers = (IList)G(G(Character, "diggers"), "diggers");
        foreach (var item in diggers) S(item, "maxLevel", 10L);
        var pods = (IList)P("ListField", dc, "pods", 1); var pod = pods[0]; S(pod, "character", Character); S(pod, "id", 0);
        var input = Child(pod, "diggerLevelInput"); SupportInputWrite(input, "operator draft");
    }
    static object DiggerSelection(int id, long level)
    {
        var value = Activator.CreateInstance(Manager("SupportDiggerLevel"), All, null, new object[] { id, level }, null);
        var list = Array.CreateInstance(value.GetType(), 1); list.SetValue(value, 0);
        return Activator.CreateInstance(Manager("SupportSelection"), All, null, new object[] { new int[0], list }, null);
    }
    static object SupportSetup(string owner, int id, long level)
    {
        var setup = ExactSetup(CapturePitGear(), owner); S(setup, "Support", DiggerSelection(id, level));
        S(setup, "ControlsGear", false); S(setup, "ControlsSupport", true); S(setup, "ChangeSupport", true); S(setup, "Budget", 1d); S(setup, "EnforceSupportBudget", false); return setup;
    }
    static void CheckSupportClaims()
    {
        foreach (var successor in new[] { false, true }) Case(() => {
            Setup(); SetupNativeDiggers(); var diggers = (IList)G(G(Character, "diggers"), "diggers");
            var a = SupportSetup("support-A", 2, 2); M("PublishExecutionBaseline", a); M("BeginPresetSupportAcquisition", G(a, "Owner"), G(a, "Acquisition")); M("ApplySelectedSetup", a);
            var claim = BeginClaim("harvest-support", "Support, Route"); var temporary = SupportSetup("harvest-support", 8, 3); M("ApplyExecutionClaim", claim, temporary);
            Equal(3L, G(diggers[8], "curLevel"), "real native input sets temporary level"); Equal("8", string.Join(",", ((IEnumerable)G(G(Character, "diggers"), "activeDiggers")).Cast<object>()), "native activation selects temporary digger");
            if (successor)
            {
                var b = SupportSetup("support-current-B", 11, 4); M("PublishExecutionBaseline", b); M("BeginPresetSupportAcquisition", G(b, "Owner"), G(b, "Acquisition")); M("ApplySelectedSetup", b);
                Equal(true, G(diggers[8], "active"), "current B remains covered by admitted temporary support");
            }
            else M("PublishExecutionBaseline", (object)null);
            var renders = DiggerRenders; var recalls = G(typeof(Program), "Recalls"); Equal(true, M("EndExecutionClaim", claim, true), "support exit resolves actual current intent");
            Equal(successor ? "11" : "8", string.Join(",", ((IEnumerable)G(G(Character, "diggers"), "activeDiggers")).Cast<object>()), "support exit never reactivates historical A");
            Equal(successor ? 4L : 3L, G(diggers[successor ? 11 : 8], "curLevel"), "current exact native level retained");
            Equal(2L, G(diggers[2], "curLevel"), "inactive old digger level remains progression, not a restoration target");
            Equal(recalls, G(typeof(Program), "Recalls"), "support handoff does not recall resources");
            if (!successor) Equal(renders, DiggerRenders, "no successor performs no support write");
            var pod = ((IList)G(G(Character, "allDiggers"), "pods"))[0]; Equal(0, G(pod, "id"), "native borrowed input selection returned narrowly"); Equal("operator draft", SupportInputRead(G(pod, "diggerLevelInput")), "unrelated operator input draft preserved");
        });
        foreach (var fault in new[] { "partial", "foreign", "epoch" }) Case(() => {
            Setup(); SetupNativeDiggers(); var claim = BeginClaim("native-support", "Support, Route"); var setup = SupportSetup("native-support", 8, 3);
            var fired = false;
            DiggerRendered = () => {
                if (fired) return; fired = true;
                if (fault == "partial") throw new InvalidOperationException("native digger clear committed before scene failure");
                if (fault == "epoch") S(G(Character, "stats"), "rebirthNumber", 8L);
                else S(((IList)G(G(Character, "diggers"), "diggers"))[11], "curLevel", 2L);
            };
            var error = Failure(() => M("ApplyExecutionClaim", claim, setup)); DiggerRendered = null;
            Check(error != null && fired, "actual native support callback stops dependent setup: " + fault);
            Equal(false, M("FiniteActionSetupVerified", claim, setup), "unverified support cannot authorize a finite effect");
            var observed = G(M("CaptureSupportState"), "StableKey"); var renders = DiggerRenders;
            if (fault == "partial")
            {
                M("ApplyExecutionClaim", claim, setup); Equal(true, M("FiniteActionSetupVerified", claim, setup), "known support prefix can complete same acquired selection");
            }
            else
            {
                Failure(() => M("ApplyExecutionClaim", claim, setup)); Equal(renders, DiggerRenders, "foreign support/epoch never repeats native clear");
                Equal(observed, G(M("CaptureSupportState"), "StableKey"), "foreign native support values preserved");
            }
        });
    }
}
