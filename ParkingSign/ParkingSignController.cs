using System.Collections.Generic;
using KSerialization;
using UnityEngine;

namespace ParkingSign;

[SerializationConfig(MemberSerialization.OptIn)]
public sealed class ParkingSignController : KMonoBehaviour, ISaveLoadable, ISim1000ms
{
    private static readonly HashSet<ParkingSignController> ActiveSigns = new HashSet<ParkingSignController>();

    private static readonly Dictionary<int, ParkingSignController> WinningSignsByMinion =
        new Dictionary<int, ParkingSignController>();

    private static readonly Dictionary<int, ParkingSignController> WinningSignsByProxy =
        new Dictionary<int, ParkingSignController>();

    private static readonly HashSet<int> ReachabilityChecks = new HashSet<int>();
    private static readonly CellOffset[] SignTargetOffsets = { new CellOffset(0, 0) };
    private const int LogicValueChangedHash = -801688580;
    private static long nextSpawnOrder;

    private static readonly EventSystem.IntraObjectHandler<ParkingSignController> LogicValueChangedDelegate =
        new EventSystem.IntraObjectHandler<ParkingSignController>((component, data) =>
            component.OnLogicValueChanged(data));

    [Serialize] private List<int> selectedMinionInstanceIds = new List<int>();
    [Serialize] private int priority = -1;

    private readonly HashSet<int> commandedMinionInstanceIds = new HashSet<int>();
    private readonly HashSet<int> selectedProxyInstanceIds = new HashSet<int>();
    private long spawnOrder;
    private LogicPorts logicPorts;
    private KBatchedAnimController animController;
    private KSelectable selectable;
    private Prioritizable prioritizable;
    private bool hasPrioritizableReference;
    private int lastSignalState = -1;
    private int targetCell = Grid.InvalidCell;
    private bool isHanging;
    private GameObject ghost;

    private const int MaxHangingDrop = 10;
    private const string MinionNavGridName = "MinionNavGrid";
    private const int SelectObjectHash = (int)GameHashes.SelectObject;

    private static readonly EventSystem.IntraObjectHandler<ParkingSignController> SelectDelegate =
        new EventSystem.IntraObjectHandler<ParkingSignController>((component, data) =>
            component.OnSelect(data));

    protected override void OnSpawn()
    {
        base.OnSpawn();
        spawnOrder = nextSpawnOrder++;
        ActiveSigns.Add(this);
        KPrefabID prefabId = GetComponent<KPrefabID>();
        isHanging = prefabId != null && prefabId.PrefabTag.Name == HangingParkingSignConfig.ID;
        RefreshTargetCell();
        if (isHanging)
        {
            SimAndRenderScheduler.instance.Add(this, false);
            Subscribe(SelectObjectHash, SelectDelegate);
        }

        RefreshSelectedProxyInstanceIds();
        logicPorts = GetComponent<LogicPorts>();
        animController = GetComponent<KBatchedAnimController>();
        selectable = GetComponent<KSelectable>();
        prioritizable = GetComponent<Prioritizable>();

        if (logicPorts == null || animController == null || selectable == null || prioritizable == null)
        {
            Debug.LogError(
                $"Parking Sign '{name}' is missing a required logic port, animation controller, selectable, or prioritizable.");
            return;
        }

        prioritizable.onPriorityChanged += OnPriorityChanged;
        hasPrioritizableReference = true;
        prioritizable.AddRef();
        if (priority >= 1 && priority <= 9)
        {
            prioritizable.SetMasterPriority(
                new PrioritySetting(PriorityScreen.PriorityClass.basic, priority));
            priority = -1;
        }

        Subscribe(LogicValueChangedHash, LogicValueChangedDelegate);
        UpdateAnimation();
    }

    internal static bool TryGetForcedDestination(MinionIdentity minion, out KMonoBehaviour destination,
        out CellOffset[] offsets)
    {
        destination = null;
        offsets = null;
        KPrefabID minionPrefabId = minion != null ? minion.GetComponent<KPrefabID>() : null;
        if (minionPrefabId == null)
        {
            return false;
        }

        ParkingSignController winningSign = FindWinningReachableSign(minion);
        SetWinningSign(minionPrefabId.InstanceID, winningSign);
        Building building = winningSign != null ? winningSign.GetComponent<Building>() : null;
        int targetCell = winningSign != null ? winningSign.targetCell : Grid.InvalidCell;
        if (building == null || !Grid.IsValidCell(targetCell))
        {
            return false;
        }

        destination = building;
        Grid.CellToXY(Grid.PosToCell(winningSign.gameObject), out int signX, out int signY);
        Grid.CellToXY(targetCell, out int targetX, out int targetY);
        offsets = targetCell == Grid.PosToCell(winningSign.gameObject)
            ? SignTargetOffsets
            : new[] { new CellOffset(targetX - signX, targetY - signY) };
        return true;
    }

    internal static bool ShouldBlockPathFromSign(MinionIdentity minion, int destinationCell)
    {
        KPrefabID minionPrefabId = minion != null ? minion.GetComponent<KPrefabID>() : null;
        if (minionPrefabId == null || ReachabilityChecks.Contains(minionPrefabId.InstanceID))
        {
            return false;
        }

        int currentCell = Grid.PosToCell(minion.gameObject);
        if (!Grid.IsValidCell(currentCell) || currentCell == destinationCell)
        {
            return false;
        }

        ParkingSignController winningSign = GetCachedWinningSign(minionPrefabId.InstanceID);
        int winningSignCell = winningSign != null ? winningSign.targetCell : Grid.InvalidCell;
        return Grid.IsValidCell(winningSignCell) && currentCell == winningSignCell;
    }

    internal static bool ShouldBlockPathFromSign(int proxyInstanceId, int fromCell, int destinationCell)
    {
        if (fromCell == destinationCell)
        {
            return false;
        }

        if (ReachabilityChecks.Contains(proxyInstanceId))
        {
            return false;
        }

        ParkingSignController winningSign = GetCachedWinningSignForProxy(proxyInstanceId);
        int winningSignCell = winningSign != null ? winningSign.targetCell : Grid.InvalidCell;
        return Grid.IsValidCell(winningSignCell) && fromCell == winningSignCell;
    }

    private void RefreshSelectedProxyInstanceIds()
    {
        selectedProxyInstanceIds.Clear();
        foreach (int minionInstanceId in selectedMinionInstanceIds)
        {
            MinionIdentity minion = FindMinion(minionInstanceId);
            MinionAssignablesProxy proxy = minion != null && minion.assignableProxy != null
                ? minion.assignableProxy.Get()
                : null;
            KPrefabID proxyPrefabId = proxy != null ? proxy.GetComponent<KPrefabID>() : null;
            if (proxyPrefabId != null)
            {
                selectedProxyInstanceIds.Add(proxyPrefabId.InstanceID);
            }
        }
    }

    public bool IsMinionSelected(int instanceId)
    {
        return selectedMinionInstanceIds.Contains(instanceId);
    }

    private PrioritySetting Priority => prioritizable != null
        ? prioritizable.GetMasterPriority()
        : new PrioritySetting(PriorityScreen.PriorityClass.basic, 5);

    public void SetMinionSelected(int instanceId, bool selected)
    {
        if (selected)
        {
            if (!selectedMinionInstanceIds.Contains(instanceId))
            {
                selectedMinionInstanceIds.Add(instanceId);
            }
        }
        else
        {
            selectedMinionInstanceIds.Remove(instanceId);
        }

        RefreshSelectedProxyInstanceIds();
        ReconcileMinionAssignment(instanceId);
    }

    private void OnPriorityChanged(PrioritySetting _)
    {
        ReconcileAllSelectedMinions();
    }

    private static void ReconcileAllSelectedMinions()
    {
        HashSet<int> selectedMinions = new HashSet<int>();
        foreach (ParkingSignController sign in ActiveSigns)
        {
            if (sign != null)
            {
                selectedMinions.UnionWith(sign.selectedMinionInstanceIds);
            }
        }

        foreach (int instanceId in selectedMinions)
        {
            ReconcileMinionAssignment(instanceId);
        }
    }

    private void OnLogicValueChanged(object data)
    {
        if (((LogicValueChanged)data).portID == new HashedString(ParkingSignConfig.LogicPortId))
        {
            UpdateAnimation();
        }
    }

    private void UpdateAnimation()
    {
        int portCell = logicPorts.GetPortCell(new HashedString(ParkingSignConfig.LogicPortId));
        LogicCircuitNetwork network = Game.Instance.logicCircuitManager.GetNetworkForCell(portCell);
        int signalState = network != null && LogicCircuitNetwork.IsBitActive(0, network.OutputValue) ? 1 : 0;
        if (signalState == lastSignalState)
        {
            return;
        }

        lastSignalState = signalState;
        HashedString animation = signalState == 1 ? new HashedString("on") : new HashedString("off");
        animController.Play(animation, KAnim.PlayMode.Loop, 1f, 0f);
        RefreshStatus();

        ReconcileSelectedMinions();
    }

    private StatusItem currentStatus;

    private void RefreshStatus()
    {
        if (selectable == null || ParkingSignStatusItems.NoStandPoint == null)
        {
            return;
        }

        StatusItem item = isHanging && !Grid.IsValidCell(targetCell)
            ? ParkingSignStatusItems.NoStandPoint
            : lastSignalState == 1
                ? ParkingSignStatusItems.Active
                : ParkingSignStatusItems.Inactive;
        if (item == currentStatus)
        {
            return;
        }

        currentStatus = item;
        selectable.SetStatusItem(Db.Get().StatusItemCategories.Main, item, this);
    }

    private void ReconcileSelectedMinions()
    {
        foreach (int instanceId in selectedMinionInstanceIds)
        {
            ReconcileMinionAssignment(instanceId);
        }
    }

    private static void ReconcileMinionAssignment(int instanceId)
    {
        MinionIdentity minion = FindMinion(instanceId);
        ParkingSignController winningSign = FindWinningReachableSign(minion);
        SetWinningSign(instanceId, winningSign);
        if (winningSign == null)
        {
            foreach (ParkingSignController sign in ActiveSigns)
            {
                sign?.ReleaseMinion(instanceId);
            }

            return;
        }

        foreach (ParkingSignController sign in ActiveSigns)
        {
            if (sign != null && sign != winningSign)
            {
                sign.commandedMinionInstanceIds.Remove(instanceId);
            }
        }

        winningSign.MoveMinionToSign(instanceId);
    }

    private static ParkingSignController FindWinningReachableSign(MinionIdentity minion)
    {
        if (minion == null)
        {
            return null;
        }

        KPrefabID prefabId = minion.GetComponent<KPrefabID>();
        Navigator navigator = minion.GetComponent<Navigator>();
        if (prefabId == null || navigator == null)
        {
            return null;
        }

        int instanceId = prefabId.InstanceID;
        MinionAssignablesProxy proxy = minion.assignableProxy != null
            ? minion.assignableProxy.Get()
            : null;
        KPrefabID proxyPrefabId = proxy != null ? proxy.GetComponent<KPrefabID>() : null;
        bool addedMinion = ReachabilityChecks.Add(instanceId);
        bool addedProxy = proxyPrefabId != null && ReachabilityChecks.Add(proxyPrefabId.InstanceID);
        ParkingSignController winner = null;
        ParkingSignController cachedWinner = GetCachedWinningSign(instanceId);
        int currentCell = Grid.PosToCell(minion.gameObject);
        // The cached path probe is stale-blocked while the minion stands on its winning sign's cell.
        bool probeBlocked = cachedWinner != null && cachedWinner.targetCell == currentCell;
        try
        {
            foreach (ParkingSignController sign in ActiveSigns)
            {
                if (sign == null || sign.lastSignalState != 1 || !sign.IsMinionSelected(instanceId))
                {
                    continue;
                }

                int signTargetCell = sign.targetCell;
                if (Grid.IsValidCell(signTargetCell) &&
                    (navigator.CanReach(signTargetCell) || probeBlocked && sign != cachedWinner) &&
                    HasHigherPrecedence(sign, winner))
                {
                    winner = sign;
                }
            }
        }
        finally
        {
            if (addedMinion)
            {
                ReachabilityChecks.Remove(instanceId);
            }

            if (addedProxy)
            {
                ReachabilityChecks.Remove(proxyPrefabId.InstanceID);
            }
        }

        return winner;
    }

    private static void SetWinningSign(int instanceId, ParkingSignController sign)
    {
        if (sign == null)
        {
            WinningSignsByMinion.Remove(instanceId);
        }
        else
        {
            WinningSignsByMinion[instanceId] = sign;
        }

        MinionIdentity minion = FindMinion(instanceId);
        MinionAssignablesProxy proxy = minion != null && minion.assignableProxy != null
            ? minion.assignableProxy.Get()
            : null;
        KPrefabID proxyPrefabId = proxy != null ? proxy.GetComponent<KPrefabID>() : null;
        if (proxyPrefabId == null)
        {
            return;
        }

        if (sign == null)
        {
            WinningSignsByProxy.Remove(proxyPrefabId.InstanceID);
        }
        else
        {
            WinningSignsByProxy[proxyPrefabId.InstanceID] = sign;
        }
    }

    private static ParkingSignController GetCachedWinningSign(int instanceId)
    {
        return WinningSignsByMinion.TryGetValue(instanceId, out ParkingSignController sign) ? sign : null;
    }

    private static ParkingSignController GetCachedWinningSignForProxy(int proxyInstanceId)
    {
        return WinningSignsByProxy.TryGetValue(proxyInstanceId, out ParkingSignController sign) ? sign : null;
    }

    private static void RemoveWinningSign(ParkingSignController sign)
    {
        List<int> minionIdsToRemove = new List<int>();
        foreach (KeyValuePair<int, ParkingSignController> entry in WinningSignsByMinion)
        {
            if (entry.Value == sign)
            {
                minionIdsToRemove.Add(entry.Key);
            }
        }

        foreach (int instanceId in minionIdsToRemove)
        {
            WinningSignsByMinion.Remove(instanceId);
        }

        List<int> proxyIdsToRemove = new List<int>();
        foreach (KeyValuePair<int, ParkingSignController> entry in WinningSignsByProxy)
        {
            if (entry.Value == sign)
            {
                proxyIdsToRemove.Add(entry.Key);
            }
        }

        foreach (int proxyInstanceId in proxyIdsToRemove)
        {
            WinningSignsByProxy.Remove(proxyInstanceId);
        }
    }

    private static bool HasHigherPrecedence(ParkingSignController candidate, ParkingSignController current)
    {
        return current == null || candidate.Priority > current.Priority ||
               candidate.Priority == current.Priority && candidate.spawnOrder < current.spawnOrder;
    }

    private void MoveMinionToSign(int instanceId)
    {
        MinionIdentity minion = FindMinion(instanceId);
        if (minion == null)
        {
            return;
        }

        Navigator navigator = minion.GetComponent<Navigator>();
        if (navigator == null)
        {
            Debug.LogWarning($"Parking Sign cannot move {minion.GetProperName()}: no Navigator component.");
            return;
        }

        if (!Grid.IsValidCell(targetCell))
        {
            Debug.LogWarning($"Parking Sign '{name}' has an invalid destination cell.");
            return;
        }

        MoveToLocationMonitor.Instance moveMonitor =
            navigator.GetSMI<MoveToLocationMonitor.Instance>();
        if (moveMonitor != null)
        {
            moveMonitor.MoveToLocation(targetCell);
            commandedMinionInstanceIds.Add(instanceId);
        }
        else
        {
            Debug.LogWarning($"Parking Sign cannot move {minion.GetProperName()}: move monitor is unavailable.");
        }
    }

    private void ReleaseMinion(int instanceId)
    {
        if (!commandedMinionInstanceIds.Remove(instanceId))
        {
            return;
        }

        MinionIdentity minion = FindMinion(instanceId);
        if (minion == null)
        {
            return;
        }

        Navigator navigator = minion.GetComponent<Navigator>();
        MoveToLocationMonitor.Instance moveMonitor =
            navigator != null ? navigator.GetSMI<MoveToLocationMonitor.Instance>() : null;
        if (moveMonitor != null && moveMonitor.targetCell == targetCell)
        {
            moveMonitor.GoTo(moveMonitor.sm.satisfied);
        }

        if (navigator != null)
        {
            navigator.Stop();
        }
    }

    private static MinionIdentity FindMinion(int instanceId)
    {
        if (Components.LiveMinionIdentities == null)
        {
            return null;
        }

        foreach (MinionIdentity minion in Components.LiveMinionIdentities.Items)
        {
            KPrefabID prefabId = minion != null ? minion.GetComponent<KPrefabID>() : null;
            if (prefabId != null && prefabId.InstanceID == instanceId)
            {
                return minion;
            }
        }

        return null;
    }

    public void Sim1000ms(float dt)
    {
        int previous = targetCell;
        RefreshTargetCell();
        RefreshStatus();
        if (previous != targetCell)
        {
            ReconcileSelectedMinions();
        }

        UpdateGhost(IsSelected());
    }

    private bool IsSelected()
    {
        return SelectTool.Instance != null && selectable != null && SelectTool.Instance.selected == selectable;
    }

    private void Update()
    {
        if (isHanging)
        {
            bool selected = IsSelected();
            if (selected != (ghost != null && ghost.activeSelf))
            {
                if (selected)
                {
                    RefreshTargetCell();
                }

                UpdateGhost(selected);
            }
        }
    }

    private void RefreshTargetCell()
    {
        int signCell = Grid.PosToCell(gameObject);
        targetCell = isHanging ? FindHangingTargetCell(signCell) : signCell;
    }

    private static int FindHangingTargetCell(int signCell)
    {
        NavGrid navGrid = Pathfinding.Instance != null ? Pathfinding.Instance.GetNavGrid(MinionNavGridName) : null;
        int cell = signCell;
        for (int i = 0; i < MaxHangingDrop; i++)
        {
            cell = Grid.CellBelow(cell);
            if (!Grid.IsValidCell(cell) || Grid.Solid[cell])
            {
                break;
            }

            if (IsStandable(navGrid, cell))
            {
                return cell;
            }
        }

        return Grid.InvalidCell;
    }

    private static bool IsStandable(NavGrid navGrid, int cell)
    {
        if (navGrid == null)
        {
            int below = Grid.CellBelow(cell);
            return Grid.IsValidCell(below) && Grid.Solid[below];
        }

        return navGrid.NavTable.IsValid(cell, NavType.Floor) ||
               navGrid.NavTable.IsValid(cell, NavType.Ladder) ||
               navGrid.NavTable.IsValid(cell, NavType.Pole);
    }

    private void OnSelect(object data)
    {
        bool selected = data is bool value && value;
        if (selected)
        {
            RefreshTargetCell();
        }

        UpdateGhost(selected);
    }

    private void UpdateGhost()
    {
        UpdateGhost(ghost != null && ghost.activeSelf);
    }

    private void UpdateGhost(bool show)
    {
        if (!show || !Grid.IsValidCell(targetCell))
        {
            if (ghost != null)
            {
                ghost.SetActive(false);
            }

            return;
        }

        if (ghost == null)
        {
            ghost = CreateGhost();
            if (ghost == null)
            {
                return;
            }
        }

        ghost.transform.SetPosition(Grid.CellToPosCBC(targetCell, Grid.SceneLayer.FXFront2));
        ghost.SetActive(true);
    }

    private static GameObject CreateGhost()
    {
        Sprite vanilla = Assets.GetSprite("ui_duplicant_any_selection");
        Shader shader = Shader.Find("Sprites/Default") ?? Shader.Find("UI/Default");
        if (vanilla != null && shader != null)
        {
            GameObject root = new GameObject("ParkingSignGhost");
            root.SetActive(false);
            GameObject child = new GameObject("Sprite");
            child.transform.SetParent(root.transform, false);
            SpriteRenderer renderer = child.AddComponent<SpriteRenderer>();
            renderer.sprite = vanilla;
            renderer.material = new Material(shader);
            renderer.color = new Color(0.65f, 1f, 0.65f, 0.5f);
            renderer.sortingOrder = 1000;
            float scale = 1.6f / Mathf.Max(vanilla.bounds.size.y, 0.0001f);
            child.transform.localScale = new Vector3(scale, scale, 1f);
            child.transform.localPosition = new Vector3(0f, vanilla.bounds.extents.y * scale, 0f);
            return root;
        }

        Debug.LogWarning("Parking Sign: ui_duplicant_any_selection unavailable, using animation ghost.");
        return CreateAnimGhost();
    }

    private static GameObject CreateAnimGhost()
    {
        KAnimFile build = Assets.GetAnim("body_comp_default_kanim");
        KAnimFile anim = Assets.GetAnim("anim_idles_default_kanim");
        if (build == null || anim == null)
        {
            Debug.LogWarning("Parking Sign: Duplicant animation unavailable, using fallback silhouette.");
            return CreateSpriteGhost();
        }

        GameObject go = new GameObject("ParkingSignGhost");
        go.SetActive(false);
        KBatchedAnimController controller = go.AddComponent<KBatchedAnimController>();
        controller.AnimFiles = new[] { build, anim };
        controller.isMovable = true;
        controller.initialAnim = "idle_default";
        controller.initialMode = KAnim.PlayMode.Loop;
        controller.sceneLayer = Grid.SceneLayer.FXFront2;
        controller.visibilityType = KAnimControllerBase.VisibilityType.Always;
        controller.TintColour = new Color32(0, 0, 0, 150);
        return go;
    }

    private static GameObject CreateSpriteGhost()
    {
        Shader shader = Shader.Find("Sprites/Default") ?? Shader.Find("UI/Default");
        if (shader == null)
        {
            Debug.LogWarning("Parking Sign: no shader available for the Duplicant silhouette.");
            return null;
        }

        GameObject go = new GameObject("ParkingSignGhost");
        go.SetActive(false);
        SpriteRenderer renderer = go.AddComponent<SpriteRenderer>();
        renderer.sprite = GetSilhouetteSprite();
        renderer.material = new Material(shader);
        renderer.color = new Color(0f, 0f, 0f, 0.55f);
        renderer.sortingOrder = 1000;
        go.layer = LayerMask.NameToLayer("Default");
        return go;
    }

    private static Sprite silhouetteSprite;

    private static Sprite GetSilhouetteSprite()
    {
        if (silhouetteSprite != null)
        {
            return silhouetteSprite;
        }

        const int width = 24;
        const int height = 40;
        Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear
        };
        Color32 clear = new Color32(0, 0, 0, 0);
        Color32 solid = new Color32(255, 255, 255, 255);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bool head = (x - 12) * (x - 12) + (y - 33) * (y - 33) <= 36;
                bool body = x >= 6 && x < 18 && y >= 14 && y < 27;
                bool arms = (x >= 3 && x < 6 || x >= 18 && x < 21) && y >= 15 && y < 27;
                bool legs = (x >= 7 && x < 11 || x >= 13 && x < 17) && y < 14;
                texture.SetPixel(x, y, head || body || arms || legs ? solid : clear);
            }
        }

        texture.Apply();
        silhouetteSprite = Sprite.Create(texture, new Rect(0, 0, width, height), new Vector2(0.5f, 0f), 32f);
        return silhouetteSprite;
    }

    protected override void OnCleanUp()
    {
        if (isHanging)
        {
            SimAndRenderScheduler.instance.Remove(this);
        }

        if (ghost != null)
        {
            Destroy(ghost);
            ghost = null;
        }

        if (prioritizable != null)
        {
            prioritizable.onPriorityChanged -= OnPriorityChanged;
            if (hasPrioritizableReference)
            {
                prioritizable.RemoveRef();
                hasPrioritizableReference = false;
            }
        }

        ActiveSigns.Remove(this);
        RemoveWinningSign(this);
        foreach (int instanceId in selectedMinionInstanceIds)
        {
            ReconcileMinionAssignment(instanceId);
        }

        base.OnCleanUp();
    }
}