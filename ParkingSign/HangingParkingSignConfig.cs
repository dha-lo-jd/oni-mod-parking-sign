using UnityEngine;

namespace ParkingSign;

public sealed class HangingParkingSignConfig : IBuildingConfig
{
    public const string ID = "HangingParkingSign";

    public override BuildingDef CreateBuildingDef()
    {
        return ParkingSignConfig.CreateParkingSignBuildingDef(
            ID,
            "HangingParkingSign_kanim",
            BuildLocationRule.OnCeiling,
            STRINGS.BUILDINGS.PREFABS.HANGINGPARKINGSIGN.LOGIC_PORT,
            STRINGS.BUILDINGS.PREFABS.HANGINGPARKINGSIGN.LOGIC_PORT_ACTIVE,
            STRINGS.BUILDINGS.PREFABS.HANGINGPARKINGSIGN.LOGIC_PORT_INACTIVE);
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
}