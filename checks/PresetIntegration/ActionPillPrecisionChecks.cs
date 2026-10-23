using System;

// Shared native action group includes these compiled classifier regressions.
public static partial class PresetIntegrationChecks
{
    private static void CheckActionPillPrecisionRegressions()
    {
        var before = Assertions;
        SetupInlineStagePlan();
        var adventure = Get(Character, "adventure"); var blood = Get(Character, "bloodMagic");
        var fields = new[] { "attack", "defense", "maxHP", "regen" };
        foreach (var input in new[] {
            new[] { 0f, 0f, 0f, 0f, 3f },
            new[] { 16777216f, 33554432f, 100000000f, 16777216f, 3f },
            new[] { .125f, .25f, .5f, .125f, 17f }
        })
        {
            var action = Activator.CreateInstance(MainType.GetNestedType("PillBloodAction", All), true);
            foreach (var pair in new[] { new { Name = "Power", Value = input[0] }, new { Name = "Toughness", Value = input[1] },
                new { Name = "Health", Value = input[2] }, new { Name = "Regen", Value = input[3] }, new { Name = "Worth", Value = input[4] } })
                Set(action, pair.Name, pair.Value);
            // These stores match native add/mul/add/stfld float32 order. Do not
            // round the product separately or compare an extended-precision RHS.
            var expected = new[] { input[0] + input[4], input[1] + input[4], input[2] + input[4] * 3f, input[3] + input[4] * .03f };
            Set(blood, "bloodPoints", 0d);
            for (var i = 0; i < fields.Length; i++) Set(adventure, fields[i], expected[i]);
            Equal("AppliedVerified", Invoke(action, "Classify", "before", "after", null).ToString(), "stored native binary32 stat proof commits exactly");
            Equal("ModelAppliedRenderFailed", Invoke(action, "Classify", "before", "after", new InvalidOperationException("presentation fault")).ToString(), "exact native fields retain committed render-failure outcome");
            if (input[0] == 0f)
                Equal(unchecked((int)0x3db851eb), BitConverter.ToInt32(BitConverter.GetBytes(expected[3]), 0), "native three-point pill regen has exact expected stored bits");
            for (var i = 0; i < fields.Length; i++)
            {
                var bits = BitConverter.ToInt32(BitConverter.GetBytes(expected[i]), 0);
                foreach (var direction in new[] { -1, 1 })
                {
                    Set(adventure, fields[i], BitConverter.ToSingle(BitConverter.GetBytes(bits + direction), 0));
                    Equal("UnknownOutcome", Invoke(action, "Classify", "before", "after", null).ToString(), "adjacent-ULP foreign/partial stat must remain unknown/" + fields[i] + "/" + direction);
                }
                Set(adventure, fields[i], expected[i]);
            }
            Set(blood, "bloodPoints", 100d);
            Equal("UnknownOutcome", Invoke(action, "Classify", "before", "after", null).ToString(), "matching stats without exact blood consumption cannot commit");
        }
        Console.WriteLine("Pill binary32 classifier: " + (Assertions - before) + " assertions (classification only; native cast acceptance is the blood suite).");
    }
}
