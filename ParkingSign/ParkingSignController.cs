using System.Collections.Generic;
using KSerialization;
using UnityEngine;

namespace ParkingSign;

[SerializationConfig(MemberSerialization.OptIn)]
public sealed class ParkingSignController : KMonoBehaviour, ISaveLoadable
{
    private static readonly HashSet<ParkingSignController> ActiveSigns = new HashSet<ParkingSignController>();
    private static readonly CellOffset[] SignTargetOffsets = { new CellOffset(0, 0) };
    private const int LogicValueChangedHash = -801688580;

    private static readonly EventSystem.IntraObjectHandler<ParkingSignController> LogicValueChangedDelegate =
        new EventSystem.IntraObjectHandler<ParkingSignController>((component, data) =>
            component.OnLogicValueChanged(data));

    [Serialize] private List<int> selectedMinionInstanceIds = new List<int>();

    private readonly HashSet<int> commandedMinionInstanceIds = new HashSet<int>();
    private readonly HashSet<int> selectedProxyInstanceIds = new HashSet<int>();
    private LogicPorts logicPorts;
    private KBatchedAnimController animController;
    private KSelectable selectable;
    private int lastSignalState = -1;

    protected override void OnSpawn()
    {
        base.OnSpawn();
        ActiveSigns.Add(this);
        RefreshSelectedProxyInstanceIds();
        logicPorts = GetComponent<LogicPorts>();
        animController = GetComponent<KBatchedAnimController>();
        selectable = GetComponent<KSelectable>();

        if (logicPorts == null || animController == null || selectable == null)
        {
            Debug.LogError($"Parking Sign '{name}' is missing its logic ports, animation controller, or selectable.");
            return;
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

        foreach (ParkingSignController sign in ActiveSigns)
        {
            if (sign == null || sign.lastSignalState != 1 ||
                !sign.IsMinionSelected(minionPrefabId.InstanceID))
            {
                continue;
            }

            Building building = sign.GetComponent<Building>();
            int targetCell = Grid.PosToCell(sign.gameObject);
            if (building != null && Grid.IsValidCell(targetCell))
            {
                destination = building;
                offsets = SignTargetOffsets;
                return true;
            }
        }

        return false;
    }

    internal static bool ShouldBlockPathFromSign(MinionIdentity minion, int destinationCell)
    {
        KPrefabID minionPrefabId = minion != null ? minion.GetComponent<KPrefabID>() : null;
        if (minionPrefabId == null)
        {
            return false;
        }

        int currentCell = Grid.PosToCell(minion.gameObject);
        if (!Grid.IsValidCell(currentCell) || currentCell == destinationCell)
        {
            return false;
        }

        foreach (ParkingSignController sign in ActiveSigns)
        {
            if (sign == null || sign.lastSignalState != 1 ||
                !sign.IsMinionSelected(minionPrefabId.InstanceID))
            {
                continue;
            }

            int signCell = Grid.PosToCell(sign.gameObject);
            if (Grid.IsValidCell(signCell) && currentCell == signCell)
            {
                return true;
            }
        }

        return false;
    }

    internal static bool ShouldBlockPathFromSign(int proxyInstanceId, int fromCell, int destinationCell)
    {
        if (fromCell == destinationCell)
        {
            return false;
        }

        foreach (ParkingSignController sign in ActiveSigns)
        {
            if (sign == null || sign.lastSignalState != 1 ||
                !sign.IsProxySelected(proxyInstanceId))
            {
                continue;
            }

            int signCell = Grid.PosToCell(sign.gameObject);
            if (Grid.IsValidCell(signCell) && fromCell == signCell)
            {
                return true;
            }
        }

        return false;
    }

    private bool IsProxySelected(int proxyInstanceId)
    {
        if (selectedProxyInstanceIds.Contains(proxyInstanceId))
        {
            return true;
        }

        foreach (int minionInstanceId in selectedMinionInstanceIds)
        {
            MinionIdentity minion = FindMinion(minionInstanceId);
            MinionAssignablesProxy proxy = minion != null && minion.assignableProxy != null
                ? minion.assignableProxy.Get()
                : null;
            KPrefabID proxyPrefabId = proxy != null ? proxy.GetComponent<KPrefabID>() : null;
            if (proxyPrefabId != null && proxyPrefabId.InstanceID == proxyInstanceId)
            {
                selectedProxyInstanceIds.Add(proxyInstanceId);
                return true;
            }
        }

        return false;
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

    public void SetMinionSelected(int instanceId, bool selected)
    {
        if (selected)
        {
            if (!selectedMinionInstanceIds.Contains(instanceId))
            {
                selectedMinionInstanceIds.Add(instanceId);
            }

            RefreshSelectedProxyInstanceIds();
            if (lastSignalState == 1)
            {
                MoveMinionToSign(instanceId);
            }
        }
        else
        {
            selectedMinionInstanceIds.Remove(instanceId);
            RefreshSelectedProxyInstanceIds();
            ReleaseMinion(instanceId);
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
        selectable.SetStatusItem(
            Db.Get().StatusItemCategories.Main,
            signalState == 1 ? ParkingSignStatusItems.Active : ParkingSignStatusItems.Inactive,
            this);

        if (signalState == 1)
        {
            MoveSelectedMinionsToSign();
        }
        else
        {
            ReleaseSelectedMinions();
        }
    }

    private void MoveSelectedMinionsToSign()
    {
        foreach (int instanceId in selectedMinionInstanceIds)
        {
            MoveMinionToSign(instanceId);
        }
    }

    private void MoveMinionToSign(int instanceId)
    {
        MinionIdentity minion = FindMinion(instanceId);
        if (minion == null)
        {
            return;
        }

        Navigator navigator = minion.GetComponent<Navigator>();
        int targetCell = Grid.PosToCell(gameObject);
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

    private void ReleaseSelectedMinions()
    {
        foreach (int instanceId in selectedMinionInstanceIds)
        {
            ReleaseMinion(instanceId);
        }
    }

    private void ReleaseMinion(int instanceId)
    {
        MinionIdentity minion = FindMinion(instanceId);
        if (minion == null)
        {
            commandedMinionInstanceIds.Remove(instanceId);
            return;
        }

        Navigator navigator = minion.GetComponent<Navigator>();
        if (commandedMinionInstanceIds.Remove(instanceId))
        {
            MoveToLocationMonitor.Instance moveMonitor =
                navigator != null ? navigator.GetSMI<MoveToLocationMonitor.Instance>() : null;
            int targetCell = Grid.PosToCell(gameObject);
            if (moveMonitor != null && moveMonitor.targetCell == targetCell)
            {
                moveMonitor.GoTo(moveMonitor.sm.satisfied);
            }
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

    protected override void OnCleanUp()
    {
        ActiveSigns.Remove(this);
        ReleaseSelectedMinions();
        base.OnCleanUp();
    }
}