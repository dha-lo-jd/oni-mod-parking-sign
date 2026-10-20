using PeterHan.PLib.UI;
using UnityEngine;
using UnityEngine.UI;

namespace ParkingSign;

public sealed class ParkingSignSideScreen : SideScreenContent
{
    private ParkingSignController target;
    private GameObject listPanel;

    protected override void OnPrefabInit()
    {
        base.OnPrefabInit();
        ContentContainer = gameObject;
    }

    public override bool IsValidForTarget(GameObject target)
    {
        return target != null && target.GetComponent<ParkingSignController>() != null;
    }

    public override void SetTarget(GameObject targetObject)
    {
        target = targetObject != null ? targetObject.GetComponent<ParkingSignController>() : null;
        RebuildList();
    }

    public override void ClearTarget()
    {
        target = null;
        RebuildList();
    }

    public override string GetTitle()
    {
        return STRINGS.UI.PARKINGSIGN.SIDESCREEN_TITLE;
    }

    private void RebuildList()
    {
        if (listPanel != null)
        {
            Object.Destroy(listPanel);
            listPanel = null;
        }

        if (target == null)
        {
            return;
        }

        PPanel panel = new PPanel("ParkingSignMinionList")
        {
            Direction = PanelDirection.Vertical,
            Alignment = TextAnchor.UpperLeft,
            Margin = new RectOffset(8, 8, 4, 4),
            Spacing = 4,
            FlexSize = Vector2.right,
            BackColor = PUITuning.Colors.BackgroundLight
        };
        foreach (MinionIdentity minion in Components.LiveMinionIdentities.Items)
        {
            KPrefabID prefabId = minion.GetComponent<KPrefabID>();
            if (prefabId == null)
            {
                continue;
            }

            int instanceId = prefabId.InstanceID;
            PPanel row = new PPanel($"ParkingSignMinionRow_{instanceId}")
            {
                Direction = PanelDirection.Horizontal,
                Alignment = TextAnchor.MiddleLeft,
                Spacing = 4,
                FlexSize = Vector2.right
            };

            PCheckBox checkBox = new PCheckBox($"ParkingSignMinion_{instanceId}")
            {
                OnChecked = (source, state) => OnMinionChecked(source, state, instanceId),
                TextStyle = PUITuning.Fonts.TextDarkStyle,
                CheckColor = PUITuning.Colors.ComponentLightStyle,
                CheckSize = new Vector2f(26, 26)
            };
            checkBox.AddOnRealize(realizedCheckBox =>
                PCheckBox.SetCheckState(realizedCheckBox,
                    target.IsMinionSelected(instanceId) ? PCheckBox.STATE_CHECKED : PCheckBox.STATE_UNCHECKED));
            row.AddChild(checkBox);

            PLabel name = new PLabel($"ParkingSignMinionName_{instanceId}")
            {
                Text = minion.GetProperName(),
                TextStyle = PUITuning.Fonts.TextDarkStyle,
                FlexSize = Vector2.right
            };
            row.AddChild(name);

            row.AddOnRealize(realizedRow => { AddPortrait(realizedRow, minion, instanceId, 1); });

            panel.AddChild(row);
        }

        PScrollPane scrollPane = new PScrollPane("ParkingSignMinionScroll")
        {
            Child = panel,
            ScrollHorizontal = false,
            ScrollVertical = true,
            AlwaysShowVertical = true,
            TrackSize = 8f,
            FlexSize = Vector2.one
        };
        scrollPane.AddOnRealize(realizedPanel => listPanel = realizedPanel);
        GameObject parent = ContentContainer != null ? ContentContainer : gameObject;
        if (parent == null)
        {
            Debug.LogError("Parking Sign side screen has no UI parent to attach its duplicant list.");
            return;
        }

        scrollPane.AddTo(parent);
    }

    private static void AddPortrait(GameObject parent, MinionIdentity minion, int instanceId, int siblingIndex)
    {
        if (parent == null || minion == null)
        {
            return;
        }

        GameObject portraitPrefab = Assets.GetPrefab(MinionUIPortrait.ID);
        if (portraitPrefab == null)
        {
            Debug.LogError("Could not find the game's duplicant UI portrait prefab.");
            return;
        }

        MinionAssignablesProxy proxy = minion.assignableProxy != null
            ? minion.assignableProxy.Get()
            : null;
        IAssignableIdentity identity = proxy != null ? proxy : minion;

        KBatchedAnimController portraitController =
            Util.KInstantiateUI<KBatchedAnimController>(portraitPrefab, parent, false);
        portraitController.gameObject.name = $"ParkingSignPortrait_{instanceId}";
        portraitController.transform.SetSiblingIndex(siblingIndex);
        RectTransform rect = portraitController.transform as RectTransform;
        rect.sizeDelta = new Vector2(32f, 32f);
        LayoutElement layout = portraitController.GetComponent<LayoutElement>();
        layout.preferredWidth = 32f;
        layout.preferredHeight = 32f;

        CrewPortrait.SetPortraitData(identity, portraitController, true);
        portraitController.animScale = 0.5f;
        portraitController.SetDirty();
    }

    private void OnMinionChecked(GameObject source, int state, int instanceId)
    {
        if (target == null)
        {
            return;
        }

        bool selected = state != PCheckBox.STATE_CHECKED;
        PCheckBox.SetCheckState(source, selected ? PCheckBox.STATE_CHECKED : PCheckBox.STATE_UNCHECKED);
        target.SetMinionSelected(instanceId, selected);
    }
}