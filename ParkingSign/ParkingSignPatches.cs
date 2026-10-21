using System;
using System.Collections.Generic;
using HarmonyLib;
using PeterHan.PLib.UI;
using System.Reflection;

namespace ParkingSign;

public static class ParkingSignStatusItems
{
    public static StatusItem Active;
    public static StatusItem Inactive;
    public static StatusItem NoStandPoint;
}

[HarmonyPatch(typeof(GeneratedBuildings), nameof(GeneratedBuildings.LoadGeneratedBuildings))]
public static class GeneratedBuildings_LoadGeneratedBuildings_Patch
{
    public static void Prefix()
    {
        ModUtil.AddBuildingToPlanScreen("Utilities", ParkingSignConfig.ID);
        ModUtil.AddBuildingToPlanScreen("Utilities", HangingParkingSignConfig.ID);
    }
}

[HarmonyPatch(typeof(DetailsScreen), "OnPrefabInit")]
public static class DetailsScreen_OnPrefabInit_ParkingSign_Patch
{
    public static void Postfix()
    {
        PUIUtils.AddSideScreenContent<ParkingSignSideScreen>();
    }
}

[HarmonyPatch(typeof(Db), nameof(Db.Initialize))]
public static class Db_Initialize_ParkingSignStatusItems_Patch
{
    public static void Postfix(Db __instance)
    {
        if (ParkingSignStatusItems.Active != null && ParkingSignStatusItems.Inactive != null &&
            ParkingSignStatusItems.NoStandPoint != null)
        {
            return;
        }

        ParkingSignStatusItems.Active = __instance.BuildingStatusItems.Add(new StatusItem(
            "ParkingSignActive",
            "BUILDING",
            "",
            StatusItem.IconType.Info,
            NotificationType.Neutral,
            false,
            OverlayModes.None.ID,
            false));
        ParkingSignStatusItems.Inactive = __instance.BuildingStatusItems.Add(new StatusItem(
            "ParkingSignInactive",
            "BUILDING",
            "",
            StatusItem.IconType.Info,
            NotificationType.Neutral,
            false,
            OverlayModes.None.ID,
            false));
        ParkingSignStatusItems.NoStandPoint = __instance.BuildingStatusItems.Add(new StatusItem(
            "ParkingSignNoStandPoint",
            "BUILDING",
            "status_item_missing_foundation",
            StatusItem.IconType.Custom,
            NotificationType.Bad,
            false,
            OverlayModes.None.ID,
            true,
            63486));
    }
}

[HarmonyPatch]
public static class Grid_HasPermission_ParkingSign_Patch
{
    private static MethodBase TargetMethod()
    {
        return AccessTools.Method(typeof(Grid), nameof(Grid.HasPermission),
            new[] { typeof(int), typeof(int), typeof(int), typeof(int), typeof(NavType) });
    }

    public static bool Prefix(int cell, int minionInstanceID, int fromCell, ref bool __result)
    {
        if (!ParkingSignController.ShouldBlockPathFromSign(minionInstanceID, fromCell, cell))
        {
            return true;
        }

        __result = false;
        return false;
    }
}

[HarmonyPatch]
public static class Navigator_CanReach_ParkingSign_Patch
{
    private static MethodBase TargetMethod()
    {
        return AccessTools.Method(typeof(Navigator), nameof(Navigator.CanReach), new[] { typeof(int) });
    }

    public static bool Prefix(Navigator __instance, int __0, ref bool __result)
    {
        MinionIdentity minion = __instance != null ? __instance.GetComponent<MinionIdentity>() : null;
        if (!ParkingSignController.ShouldBlockPathFromSign(minion, __0))
        {
            return true;
        }

        __result = false;
        return false;
    }
}

[HarmonyPatch(typeof(Navigator), nameof(Navigator.AdvancePath))]
public static class Navigator_AdvancePath_ParkingSign_Patch
{
    private static readonly HashSet<Navigator> RedirectedAdvances = new HashSet<Navigator>();

    private static readonly PropertyInfo TargetOffsetsProperty =
        AccessTools.Property(typeof(Navigator), nameof(Navigator.targetOffsets));

    public struct OriginalDestination
    {
        public KMonoBehaviour Target;
        public CellOffset[] Offsets;
        public KMonoBehaviour ForcedTarget;
        public bool WasForced;
        public bool WasRedirected;
    }

    public static void Prefix(Navigator __instance, out OriginalDestination __state)
    {
        __state = default;
        MinionIdentity minion = __instance != null ? __instance.GetComponent<MinionIdentity>() : null;
        if (!ParkingSignController.TryGetForcedDestination(minion,
                out KMonoBehaviour destination, out CellOffset[] offsets))
        {
            return;
        }

        __state.Target = __instance.target;
        __state.Offsets = __instance.targetOffsets;
        __state.ForcedTarget = destination;
        __state.WasForced = true;
        __state.WasRedirected = __instance.target != null &&
                                Grid.PosToCell(__instance.target.gameObject) !=
                                Grid.PosToCell(destination.gameObject);
        if (__state.WasRedirected)
        {
            RedirectedAdvances.Add(__instance);
        }

        __instance.target = destination;
        TargetOffsetsProperty.SetValue(__instance, offsets);
    }

    public static Exception Finalizer(Navigator __instance, OriginalDestination __state, Exception __exception)
    {
        if (!__state.WasForced || __instance == null)
        {
            return __exception;
        }

        if (__state.WasRedirected)
        {
            RedirectedAdvances.Remove(__instance);
        }

        if (__instance.target == __state.ForcedTarget)
        {
            __instance.target = __state.Target;
            TargetOffsetsProperty.SetValue(__instance, __state.Offsets);
        }

        return __exception;
    }

    internal static bool IsRedirectedAdvance(Navigator navigator)
    {
        return navigator != null && RedirectedAdvances.Contains(navigator);
    }
}

[HarmonyPatch]
public static class Navigator_Stop_ParkingSign_Patch
{
    private static MethodBase TargetMethod()
    {
        return AccessTools.Method(typeof(Navigator), nameof(Navigator.Stop), new[] { typeof(bool), typeof(bool) });
    }

    public static void Prefix(Navigator __instance, ref bool arrived_at_destination)
    {
        if (!arrived_at_destination ||
            !Navigator_AdvancePath_ParkingSign_Patch.IsRedirectedAdvance(__instance))
        {
            return;
        }

        arrived_at_destination = false;
    }
}