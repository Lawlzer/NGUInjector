using System;
using System.Linq;
using System.Reflection;

// Reflection is only access to internal compiled APIs. No grant, scheduler,
// allocation, native model accounting or writer is substituted here.
internal sealed class NativePolicyFixture : IDisposable
{
    internal const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    internal static Assembly Production, Checks;
    private readonly object fixture;
    internal readonly Character Character;
    internal readonly object Plan;

    internal NativePolicyFixture(params int[] durations)
    {
        var type = Checks.GetType("ProtectedTimingIntegrationChecks", true).GetNestedType("Fixture", All);
        fixture = Activator.CreateInstance(type, All, null, new object[] {Production, durations}, null);
        try
        {
            Fixture("PrepareApplication");
            Character = (Character) Get(fixture, "Character");
            Plan = Get(fixture, "Plan");
            Fixture("Setting", "ManageMagic", true);
            PolicyChecks.SeedAugments(Character);
        }
        catch { Dispose(); throw; }
    }

    internal object Stage(int index = 0) { return Fixture("Stage", index); }
    internal object Fixture(string name, params object[] args) { return Call(fixture, name, args); }
    internal object Main(string name, params object[] args) { return Fixture("Call", name, args); }
    internal object Apply(object stage, int seconds = 300, string context = "ordinary", int duration = 300)
    {
        return Static("CustomStageAllocator", "ApplyStage", Plan, stage, seconds, 0, context, duration);
    }
    internal static object Static(string type, string name, params object[] args)
    {
        return Call(Production.GetType("NGUInjector.Managers." + type, true), name, args);
    }
    internal static object Optimize(int seconds, long energy, int lookahead = 5, int tier = -1, bool upgrade = true)
    {
        return Static("AugmentOptimizer", "OptimizeForActualBudget", seconds, energy, lookahead, tier, upgrade);
    }
    internal static object Call(object target, string name, params object[] args)
    {
        var type = target as Type ?? target.GetType();
        var methods = type.GetMethods(All).Where(x => x.Name == name && x.GetParameters().Length == args.Length);
        var method = methods.Single(x => x.GetParameters().Select((p, i) => args[i] == null || p.ParameterType.IsInstanceOfType(args[i])
            || p.ParameterType.IsByRef).All(matches => matches));
        try { return method.Invoke(target is Type ? null : target, args); }
        catch (TargetInvocationException error) when (error.InnerException != null)
        {
            Exception cause = error;
            while (cause is TargetInvocationException && cause.InnerException != null) cause = cause.InnerException;
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(cause).Throw(); throw;
        }
    }
    internal static object Get(object target, string name)
    {
        var type = target as Type ?? target.GetType();
        var field = Field(type, name);
        return field != null ? field.GetValue(target is Type ? null : target)
            : type.GetProperty(name, All).GetValue(target is Type ? null : target);
    }
    internal static T Get<T>(object target, string name) { return (T) Get(target, name); }
    internal static void Set(object target, string name, object value)
    {
        var type = target.GetType(); var field = Field(type, name);
        if (field != null) field.SetValue(target, value);
        else type.GetProperty(name, All).SetValue(target, value);
    }
    private static FieldInfo Field(Type type, string name)
    {
        for (; type != null; type = type.BaseType)
        {
            var field = type.GetField(name, All);
            if (field != null) return field;
        }
        return null;
    }
    internal static object NewStage()
    {
        return Activator.CreateInstance(Production.GetType("NGUInjector.CustomPlans.CustomPlanStage", true));
    }
    public void Dispose() { ((IDisposable) fixture).Dispose(); }
}
