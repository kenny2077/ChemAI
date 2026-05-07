using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Aggregates the four free-lab experiment states into a single summary that
/// the chemistry agent can use for answers, hints, and warnings.
/// </summary>
public class LabScene2ExperimentStateHub : MonoBehaviour
{
    public enum StationProgress
    {
        Ready,
        InProgress,
        Completed,
        Failed
    }

    [Serializable]
    public sealed class ExperimentStateSnapshot
    {
        public string summary;
        public bool hasInterestingChange;
        public bool hasRiskOrFailure;
        public string focusEvent;
        public string copperSulfateState;
        public string sodiumChlorideState;
        public string aluminumIodideState;
        public string calciumHydroxideState;
    }

    [SerializeField] private bool enableOnStart = true;
    [SerializeField] private float pollIntervalSeconds = 0.5f;
    [SerializeField] private float litmusTolerance = 0.05f;

    private ControlReactions controlReactions;
    private LabScene2FreeModeFailureManager failureManager;

    private PourH2so4 sulfuricAcidPour;
    private PourCuO copperOxidePour;
    private PourHCL hydrochloricAcidPour;
    private PourNahco3 sodiumBicarbonatePour;
    private PourCuO iodinePour;
    private PourCuO aluminumPour;
    private FillPipette fillPipette;
    private PourFromPipette pipettePour;
    private PourSubstance calciumWaterPour;
    private PourCuO calciumOxidePour;
    private reactionCaOH calciumHydroxideReaction;

    private float nextPollTime;
    private ExperimentStateSnapshot latestSnapshot;
    private string lastSummary = string.Empty;

    public event Action<ExperimentStateSnapshot> SnapshotChanged;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        Scene activeScene = SceneManager.GetActiveScene();
        if (!LabScene2SceneUtility.IsFreeModeScene(activeScene))
        {
            return;
        }

        if (UnityObjectCompat.FindFirstObjectByType<LabScene2ExperimentStateHub>(true) != null)
        {
            return;
        }

        GameObject host = GameObject.Find("MR_Manager");
        if (host == null)
        {
            host = new GameObject("MR_Manager");
        }

        host.AddComponent<LabScene2ExperimentStateHub>();
    }

    private void Awake()
    {
        if (UnityObjectCompat.FindObjectsByType<LabScene2ExperimentStateHub>(true).Length > 1)
        {
            Destroy(this);
        }
    }

    private IEnumerator Start()
    {
        if (!enableOnStart || !LabScene2SceneUtility.IsFreeModeScene(SceneManager.GetActiveScene()))
        {
            enabled = false;
            yield break;
        }

        yield return null;
        RefreshReferences();
        PublishSnapshot(force: true);
    }

    private void Update()
    {
        if (!enabled || Time.unscaledTime < nextPollTime)
        {
            return;
        }

        nextPollTime = Time.unscaledTime + Mathf.Max(0.1f, pollIntervalSeconds);
        RefreshReferences();
        PublishSnapshot(force: false);
    }

    public ExperimentStateSnapshot GetLatestSnapshot()
    {
        if (latestSnapshot == null)
        {
            RefreshReferences();
            latestSnapshot = BuildSnapshot(forceInterestingChange: false);
        }

        return latestSnapshot;
    }

    public string BuildStateSummary()
    {
        return GetLatestSnapshot().summary;
    }

    private void RefreshReferences()
    {
        controlReactions ??= UnityObjectCompat.FindFirstObjectByType<ControlReactions>(true);
        failureManager ??= UnityObjectCompat.FindFirstObjectByType<LabScene2FreeModeFailureManager>(true);
        calciumHydroxideReaction ??= UnityObjectCompat.FindFirstObjectByType<reactionCaOH>(true);

        if (controlReactions == null)
        {
            return;
        }

        sulfuricAcidPour ??= GetComponentIfPresent<PourH2so4>(controlReactions.h2so4Recipient);
        copperOxidePour ??= GetComponentIfPresent<PourCuO>(controlReactions.cuoRecipient);
        hydrochloricAcidPour ??= GetComponentIfPresent<PourHCL>(controlReactions.hclRecipient);
        sodiumBicarbonatePour ??= GetComponentIfPresent<PourNahco3>(controlReactions.nahco3Recipient);
        iodinePour ??= GetComponentIfPresent<PourCuO>(controlReactions.iodineRecipient);
        aluminumPour ??= GetComponentIfPresent<PourCuO>(controlReactions.aluminumRecipient);
        fillPipette ??= GetComponentIfPresent<FillPipette>(controlReactions.pipette);
        pipettePour ??= GetComponentIfPresent<PourFromPipette>(controlReactions.pipette);
        calciumWaterPour ??= GetComponentIfPresent<PourSubstance>(controlReactions.waterRecipient4);
        calciumOxidePour ??= GetComponentIfPresent<PourCuO>(controlReactions.CaO_container);
    }

    private void PublishSnapshot(bool force)
    {
        ExperimentStateSnapshot snapshot = BuildSnapshot(forceInterestingChange: force);
        latestSnapshot = snapshot;

        if (!force && snapshot.summary == lastSummary)
        {
            return;
        }

        snapshot.hasInterestingChange = force || snapshot.summary != lastSummary;
        lastSummary = snapshot.summary;
        SnapshotChanged?.Invoke(snapshot);
    }

    private ExperimentStateSnapshot BuildSnapshot(bool forceInterestingChange)
    {
        ExperimentStateSnapshot snapshot = new ExperimentStateSnapshot();

        StationProgress copperState = ResolveCopperSulfateState(out string copperDescription, out string copperEvent);
        StationProgress sodiumState = ResolveSodiumChlorideState(out string sodiumDescription, out string sodiumEvent);
        StationProgress aluminumState = ResolveAluminumIodideState(out string aluminumDescription, out string aluminumEvent);
        StationProgress calciumState = ResolveCalciumHydroxideState(out string calciumDescription, out string calciumEvent);

        snapshot.copperSulfateState = copperDescription;
        snapshot.sodiumChlorideState = sodiumDescription;
        snapshot.aluminumIodideState = aluminumDescription;
        snapshot.calciumHydroxideState = calciumDescription;
        snapshot.hasInterestingChange = forceInterestingChange;
        snapshot.hasRiskOrFailure =
            copperState == StationProgress.Failed
            || sodiumState == StationProgress.Failed
            || aluminumState == StationProgress.Failed
            || calciumState == StationProgress.Failed;

        snapshot.focusEvent = SelectFocusEvent(
            copperState, copperEvent,
            sodiumState, sodiumEvent,
            aluminumState, aluminumEvent,
            calciumState, calciumEvent);
        snapshot.summary = BuildSummaryText(
            copperDescription,
            sodiumDescription,
            aluminumDescription,
            calciumDescription,
            snapshot.focusEvent);

        return snapshot;
    }

    private StationProgress ResolveCopperSulfateState(out string description, out string focusEvent)
    {
        focusEvent = string.Empty;
        if (failureManager != null && failureManager.CopperSulfateStatus == LabScene2FreeModeFailureManager.StationState.Failed)
        {
            description = "Copper sulfate station: failed because the sulfuric acid setup was contaminated.";
            focusEvent = "The copper sulfate station is in a failed state and should warn the student not to continue with contaminated reagents.";
            return StationProgress.Failed;
        }

        bool acidAdded = sulfuricAcidPour != null && sulfuricAcidPour.containsHCL;
        bool oxideAdded = copperOxidePour != null && copperOxidePour.containsCuO;
        if (acidAdded && oxideAdded)
        {
            description = "Copper sulfate station: completed. Sulfuric acid and copper oxide reacted to form copper(II) sulfate.";
            focusEvent = "The copper sulfate station has just completed.";
            return StationProgress.Completed;
        }

        if (acidAdded)
        {
            description = "Copper sulfate station: in progress. Sulfuric acid is already in the beaker and copper oxide is still missing.";
            focusEvent = "The copper sulfate station is waiting for copper oxide.";
            return StationProgress.InProgress;
        }

        if (oxideAdded)
        {
            description = "Copper sulfate station: in progress. Copper oxide is present and sulfuric acid still needs to be added.";
            focusEvent = "The copper sulfate station is waiting for sulfuric acid.";
            return StationProgress.InProgress;
        }

        description = "Copper sulfate station: ready. Sulfuric acid and copper oxide are both untouched.";
        return StationProgress.Ready;
    }

    private StationProgress ResolveSodiumChlorideState(out string description, out string focusEvent)
    {
        focusEvent = string.Empty;
        if (failureManager != null && failureManager.SodiumChlorideStatus == LabScene2FreeModeFailureManager.StationState.Failed)
        {
            description = "Sodium chloride station: failed because the setup was spoiled by the wrong solid.";
            focusEvent = "The sodium chloride station is in a failed state and should warn the student to reset it.";
            return StationProgress.Failed;
        }

        bool acidAdded = hydrochloricAcidPour != null && hydrochloricAcidPour.containsHCL;
        bool bicarbonateAdded = sodiumBicarbonatePour != null && sodiumBicarbonatePour.containsNahco3;
        if (acidAdded && bicarbonateAdded)
        {
            description = "Sodium chloride station: completed. Hydrochloric acid and sodium bicarbonate reacted and released carbon dioxide.";
            focusEvent = "The sodium chloride station has just completed.";
            return StationProgress.Completed;
        }

        if (acidAdded)
        {
            description = "Sodium chloride station: in progress. Hydrochloric acid is in place and sodium bicarbonate is still missing.";
            focusEvent = "The sodium chloride station is waiting for sodium bicarbonate.";
            return StationProgress.InProgress;
        }

        if (bicarbonateAdded)
        {
            description = "Sodium chloride station: in progress. Sodium bicarbonate is present and hydrochloric acid still needs to be added.";
            focusEvent = "The sodium chloride station is waiting for hydrochloric acid.";
            return StationProgress.InProgress;
        }

        description = "Sodium chloride station: ready. Hydrochloric acid and sodium bicarbonate are untouched.";
        return StationProgress.Ready;
    }

    private StationProgress ResolveAluminumIodideState(out string description, out string focusEvent)
    {
        focusEvent = string.Empty;
        if (failureManager != null && failureManager.AluminumIodideStatus == LabScene2FreeModeFailureManager.StationState.Failed)
        {
            description = "Aluminum iodide station: failed because water was added before the solids were prepared correctly.";
            focusEvent = "The aluminum iodide station is in a failed state and should warn the student not to keep adding reagents.";
            return StationProgress.Failed;
        }

        bool aluminumAdded = aluminumPour != null && aluminumPour.containsCuO;
        bool iodineAdded = iodinePour != null && iodinePour.containsCuO;
        bool pipetteFilled = fillPipette != null && fillPipette.pipetteIsFull;
        bool waterAdded = pipettePour != null && pipettePour.containsWater;

        if (aluminumAdded && iodineAdded && waterAdded)
        {
            description = "Aluminum iodide station: completed. Aluminum and iodine were combined and then activated with water.";
            focusEvent = "The aluminum iodide station has just completed.";
            return StationProgress.Completed;
        }

        if (aluminumAdded && iodineAdded)
        {
            description = "Aluminum iodide station: in progress. Both solids are in the crystallizing dish and the next step is adding water with the pipette."
                + (pipetteFilled ? " The pipette is currently filled." : " The pipette still needs to be filled.");
            focusEvent = "The aluminum iodide station is waiting for pipette water.";
            return StationProgress.InProgress;
        }

        if (aluminumAdded)
        {
            description = "Aluminum iodide station: in progress. Aluminum has been added and iodine is still missing.";
            focusEvent = "The aluminum iodide station is waiting for iodine.";
            return StationProgress.InProgress;
        }

        if (iodineAdded)
        {
            description = "Aluminum iodide station: in progress. Iodine has been added and aluminum is still missing.";
            focusEvent = "The aluminum iodide station is waiting for aluminum.";
            return StationProgress.InProgress;
        }

        description = "Aluminum iodide station: ready. Aluminum, iodine, and pipette water have not been combined yet.";
        return StationProgress.Ready;
    }

    private StationProgress ResolveCalciumHydroxideState(out string description, out string focusEvent)
    {
        focusEvent = string.Empty;
        if (failureManager != null && failureManager.CalciumHydroxideStatus == LabScene2FreeModeFailureManager.StationState.Failed)
        {
            description = "Calcium hydroxide station: failed because the litmus test was attempted before the base formed.";
            focusEvent = "The calcium hydroxide station is in a failed state and should warn the student to reset the station.";
            return StationProgress.Failed;
        }

        bool waterAdded = calciumWaterPour != null && calciumWaterPour.containsWater;
        bool oxideAdded = calciumOxidePour != null && calciumOxidePour.containsCuO;
        bool litmusTested = IsLitmusAtReactionTarget();

        if (waterAdded && oxideAdded && litmusTested)
        {
            description = "Calcium hydroxide station: completed. Calcium oxide reacted with water and the litmus test confirmed a basic solution.";
            focusEvent = "The calcium hydroxide station has just completed.";
            return StationProgress.Completed;
        }

        if (waterAdded && oxideAdded)
        {
            description = "Calcium hydroxide station: in progress. Calcium hydroxide has formed and the red litmus paper still needs to be tested.";
            focusEvent = "The calcium hydroxide station is waiting for the litmus test.";
            return StationProgress.InProgress;
        }

        if (waterAdded)
        {
            description = "Calcium hydroxide station: in progress. Water has been added and calcium oxide is still missing.";
            focusEvent = "The calcium hydroxide station is waiting for calcium oxide.";
            return StationProgress.InProgress;
        }

        if (oxideAdded)
        {
            description = "Calcium hydroxide station: in progress. Calcium oxide is present and water still needs to be added.";
            focusEvent = "The calcium hydroxide station is waiting for water.";
            return StationProgress.InProgress;
        }

        description = "Calcium hydroxide station: ready. Water, calcium oxide, and the litmus test are untouched.";
        return StationProgress.Ready;
    }

    private bool IsLitmusAtReactionTarget()
    {
        if (calciumHydroxideReaction == null
            || calciumHydroxideReaction.TurnesolPivot == null
            || calciumHydroxideReaction.CaOHPivot == null)
        {
            return false;
        }

        Vector3 litmusPosition = calciumHydroxideReaction.TurnesolPivot.transform.position;
        Vector3 targetPosition = calciumHydroxideReaction.CaOHPivot.transform.position;
        return Mathf.Abs(litmusPosition.x - targetPosition.x) <= litmusTolerance
            && Mathf.Abs(litmusPosition.z - targetPosition.z) <= litmusTolerance
            && Mathf.Abs(litmusPosition.y - targetPosition.y) <= 0.02f;
    }

    private static string BuildSummaryText(
        string copperDescription,
        string sodiumDescription,
        string aluminumDescription,
        string calciumDescription,
        string focusEvent)
    {
        StringBuilder builder = new StringBuilder();
        builder.AppendLine("Current free-lab state:");
        builder.AppendLine(copperDescription);
        builder.AppendLine(sodiumDescription);
        builder.AppendLine(aluminumDescription);
        builder.AppendLine(calciumDescription);

        if (!string.IsNullOrEmpty(focusEvent))
        {
            builder.AppendLine("Most relevant change: " + focusEvent);
        }

        return builder.ToString().Trim();
    }

    private static string SelectFocusEvent(
        StationProgress copperState, string copperEvent,
        StationProgress sodiumState, string sodiumEvent,
        StationProgress aluminumState, string aluminumEvent,
        StationProgress calciumState, string calciumEvent)
    {
        string failedEvent = FirstMatchingEvent(StationProgress.Failed, copperState, copperEvent, sodiumState, sodiumEvent, aluminumState, aluminumEvent, calciumState, calciumEvent);
        if (!string.IsNullOrWhiteSpace(failedEvent))
        {
            return failedEvent;
        }

        string inProgressEvent = FirstMatchingEvent(StationProgress.InProgress, copperState, copperEvent, sodiumState, sodiumEvent, aluminumState, aluminumEvent, calciumState, calciumEvent);
        if (!string.IsNullOrWhiteSpace(inProgressEvent))
        {
            return inProgressEvent;
        }

        return FirstMatchingEvent(StationProgress.Completed, copperState, copperEvent, sodiumState, sodiumEvent, aluminumState, aluminumEvent, calciumState, calciumEvent);
    }

    private static string FirstMatchingEvent(
        StationProgress desiredState,
        StationProgress state1, string event1,
        StationProgress state2, string event2,
        StationProgress state3, string event3,
        StationProgress state4, string event4)
    {
        if (state1 == desiredState && !string.IsNullOrWhiteSpace(event1))
        {
            return event1;
        }

        if (state2 == desiredState && !string.IsNullOrWhiteSpace(event2))
        {
            return event2;
        }

        if (state3 == desiredState && !string.IsNullOrWhiteSpace(event3))
        {
            return event3;
        }

        if (state4 == desiredState && !string.IsNullOrWhiteSpace(event4))
        {
            return event4;
        }

        return string.Empty;
    }

    private static string FirstNonEmpty(params string[] values)
    {
        foreach (string value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return string.Empty;
    }

    private static T GetComponentIfPresent<T>(GameObject sceneObject) where T : Component
    {
        return sceneObject != null ? sceneObject.GetComponent<T>() : null;
    }
}
