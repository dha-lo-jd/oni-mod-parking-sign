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
        BuildingDef def = BuildingTemplates.CreateBuildingDef(
            ID,
            1,
            1,
            "ParkingSign_kanim",
            BUILDINGS.HITPOINTS.TIER2,
            BUILDINGS.CONSTRUCTION_TIME_SECONDS.TIER1,
            new[] { 25f },
            new[] { MATERIALS.BUILDABLERAW },
            BUILDINGS.MELTING_POINT_KELVIN.TIER1,
            BuildLocationRule.OnFloor,
            DECOR.NONE,
            NOISE_POLLUTION.NONE);

        def.Overheatable = false;
        def.LogicInputPorts = new List<LogicPorts.Port>
        {
            LogicPorts.Port.InputPort(
                LogicPortId,
                new CellOffset(0, 0),
                (string)STRINGS.BUILDINGS.PREFABS.PARKINGSIGN.LOGIC_PORT,
                (string)STRINGS.BUILDINGS.PREFABS.PARKINGSIGN.LOGIC_PORT_ACTIVE,
                (string)STRINGS.BUILDINGS.PREFABS.PARKINGSIGN.LOGIC_PORT_INACTIVE)
        };

        return def;
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
        go.AddOrGet<ParkingSignController>();
    }
}