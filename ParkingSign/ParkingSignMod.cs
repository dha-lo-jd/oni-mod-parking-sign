using HarmonyLib;
using KMod;

namespace ParkingSign;

public sealed class ParkingSignMod : UserMod2
{
    public override void OnLoad(Harmony harmony)
    {
        base.OnLoad(harmony);
    }

    [HarmonyPatch(typeof(Localization), nameof(Localization.Initialize))]
    private static class Localization_Initialize_Patch
    {
        private static void Postfix()
        {
            Localization.RegisterForTranslation(typeof(STRINGS));
            LocString.CreateLocStringKeys(typeof(STRINGS), null);
        }
    }
}