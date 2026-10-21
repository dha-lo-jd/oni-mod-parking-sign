using System.Collections.Generic;
using TUNING;
using UnityEngine;

namespace ParkingSign;

public sealed class ParkingSignConfig : IBuildingConfig
{
    public const string ID = "ParkingSign";
    public const string LogicPortId = "PARKING_SIGN_INPUT";

    public override BuildingDef CreateBuildingDef()
    {
        return CreateParkingSignBuildingDef(ID, "ParkingSign_kanim", BuildLocationRule.OnFloor,
            STRINGS.BUILDINGS.PREFABS.PARKINGSIGN.LOGIC_PORT,
            STRINGS.BUILDINGS.PREFABS.PARKINGSIGN.LOGIC_PORT_ACTIVE,
            STRINGS.BUILDINGS.PREFABS.PARKINGSIGN.LOGIC_PORT_INACTIVE);
    }

    public override void ConfigureBuildingTemplate(GameObject go, Tag prefab_tag)
    {
    }

    public override void DoPostConfigurePreview(BuildingDef def, GameObject go)
    {
    }

    public override void DoPostConfigureUnderConstruction(GameObject go)
    {
    }

    public override void DoPostConfigureComplete(GameObject go)
    {
        go.AddOrGet<Prioritizable>();
        go.AddOrGet<ParkingSignController>();
    }

    internal static BuildingDef CreateParkingSignBuildingDef(string id, string anim, BuildLocationRule locationRule,
        LocString portName, LocString activeLabel, LocString inactiveLabel)
    {
        BuildingDef def = BuildingTemplates.CreateBuildingDef(
            id,
            1,
            1,
            anim,
            BUILDINGS.HITPOINTS.TIER2,
            BUILDINGS.CONSTRUCTION_TIME_SECONDS.TIER1,
            new[] { 25f },
            new[] { MATERIALS.BUILDABLERAW },
            BUILDINGS.MELTING_POINT_KELVIN.TIER1,
            locationRule,
            DECOR.NONE,
            NOISE_POLLUTION.NONE);

        def.Overheatable = false;
        def.LogicInputPorts = new List<LogicPorts.Port>
        {
            LogicPorts.Port.InputPort(
                LogicPortId,
                new CellOffset(0, 0),
                (string)portName,
                (string)activeLabel,
                (string)inactiveLabel)
        };

        return def;
    }
}