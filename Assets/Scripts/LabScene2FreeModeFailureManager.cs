using System;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Adds station-specific failure rules to the free-play Lab Scene 2 variant.
/// Each station can fail independently if the player uses the wrong chemistry
/// step, while the other stations stay playable.
/// </summary>
public class LabScene2FreeModeFailureManager : MonoBehaviour
{
    public enum StationState
    {
        Ready,
        Completed,
        Failed
    }

    [SerializeField] private bool enableOnStart = true;
    [SerializeField] private float wrongMixTolerance = 0.12f;
    [SerializeField] private float litmusTestTolerance = 0.05f;
    [SerializeField] private float hazardReminderDurationSeconds = 4f;
    [SerializeField] private float hazardReminderHorizontalTolerance = 0.28f;
    [SerializeField] private float hazardReminderVerticalTolerance = 0.24f;

    private const string AluminumSulfuricHazardMessage =
        "Dangerous experiment reminder: do not add aluminum powder to sulfuric acid in this lab. "
        + "That combination can produce hydrogen gas, which is highly flammable. "
        + "Return to the intended experiment steps instead.";

    private ControlReactions controlReactions;
    private TMP_Text canvasText;
    private GameObject popupWindow;
    private AudioSource guidanceAudio;

    private Reaction_h2so4_cuo copperSulfateReaction;
    private Reaction_hcl_nahco3 sodiumChlorideReaction;
    private ReactionAli3 aluminumIodideReaction;
    private reactionCaOH calciumHydroxideReaction;

    private PourH2so4 sulfuricAcidPour;
    private PourCuO copperOxidePour;
    private PourHCL hydrochloricAcidPour;
    private PourNahco3 sodiumBicarbonatePour;
    private PourCuO iodinePour;
    private PourCuO aluminumPour;
    private PourFromPipette pipettePour;
    private PourSubstance calciumWaterPour;
    private PourCuO calciumOxidePour;

    private StationState copperSulfateState;
    private StationState sodiumChlorideState;
    private StationState aluminumIodideState;
    private StationState calciumHydroxideState;
    private bool allStationsAnnounced;
    private bool aluminumSulfuricWarningShown;
    private string transientWarningMessage = string.Empty;
    private float transientWarningHideTime;

    public event Action<string> StationFailed;
    public event Action<string> SystemMessageShown;
    public event Action<string> SafetyReminderIssued;

    public StationState CopperSulfateStatus => copperSulfateState;
    public StationState SodiumChlorideStatus => sodiumChlorideState;
    public StationState AluminumIodideStatus => aluminumIodideState;
    public StationState CalciumHydroxideStatus => calciumHydroxideState;
    public string LastSystemMessage { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        Scene activeScene = SceneManager.GetActiveScene();
        if (!LabScene2SceneUtility.IsFreeModeScene(activeScene))
        {
            return;
        }

        if (UnityObjectCompat.FindFirstObjectByType<LabScene2FreeModeFailureManager>(true) != null)
        {
            return;
        }

        GameObject host = GameObject.Find("MR_Manager");
        if (host == null)
        {
            host = new GameObject("MR_Manager");
        }

        host.AddComponent<LabScene2FreeModeFailureManager>();
    }

    private void Awake()
    {
        if (UnityObjectCompat.FindObjectsByType<LabScene2FreeModeFailureManager>(true).Length > 1)
        {
            Destroy(this);
        }
    }

    private void Start()
    {
        enabled = enableOnStart && LabScene2SceneUtility.IsFreeModeScene(SceneManager.GetActiveScene());
        if (!enabled)
        {
            return;
        }

        RefreshReferences();
    }

    private void Update()
    {
        if (!enabled)
        {
            return;
        }

        RefreshReferences();
        UpdateCompletionStates();
        UpdateFailureStates();
        UpdateHazardWarnings();
        AnnounceAllStationsComplete();
    }

    private void LateUpdate()
    {
        if (!enabled)
        {
            return;
        }

        ReassertTransientWarningIfActive();
    }

    private void RefreshReferences()
    {
        controlReactions ??= UnityObjectCompat.FindFirstObjectByType<ControlReactions>(true);
        if (controlReactions == null)
        {
            return;
        }

        canvasText = controlReactions.canvasText;
        popupWindow = controlReactions.popupWindow;
        guidanceAudio = controlReactions.audioSource_guidance;

        copperSulfateReaction ??= UnityObjectCompat.FindFirstObjectByType<Reaction_h2so4_cuo>(true);
        sodiumChlorideReaction ??= UnityObjectCompat.FindFirstObjectByType<Reaction_hcl_nahco3>(true);
        aluminumIodideReaction ??= UnityObjectCompat.FindFirstObjectByType<ReactionAli3>(true);
        calciumHydroxideReaction ??= UnityObjectCompat.FindFirstObjectByType<reactionCaOH>(true);

        sulfuricAcidPour ??= controlReactions.h2so4Recipient != null ? controlReactions.h2so4Recipient.GetComponent<PourH2so4>() : null;
        copperOxidePour ??= controlReactions.cuoRecipient != null ? controlReactions.cuoRecipient.GetComponent<PourCuO>() : null;
        hydrochloricAcidPour ??= controlReactions.hclRecipient != null ? controlReactions.hclRecipient.GetComponent<PourHCL>() : null;
        sodiumBicarbonatePour ??= controlReactions.nahco3Recipient != null ? controlReactions.nahco3Recipient.GetComponent<PourNahco3>() : null;
        iodinePour ??= controlReactions.iodineRecipient != null ? controlReactions.iodineRecipient.GetComponent<PourCuO>() : null;
        aluminumPour ??= controlReactions.aluminumRecipient != null ? controlReactions.aluminumRecipient.GetComponent<PourCuO>() : null;
        pipettePour ??= controlReactions.pipette != null ? controlReactions.pipette.GetComponent<PourFromPipette>() : null;
        calciumWaterPour ??= controlReactions.waterRecipient4 != null ? controlReactions.waterRecipient4.GetComponent<PourSubstance>() : null;
        calciumOxidePour ??= controlReactions.CaO_container != null ? controlReactions.CaO_container.GetComponent<PourCuO>() : null;
    }

    private void UpdateCompletionStates()
    {
        if (copperSulfateState == StationState.Ready
            && sulfuricAcidPour != null
            && copperOxidePour != null
            && sulfuricAcidPour.containsHCL
            && copperOxidePour.containsCuO)
        {
            copperSulfateState = StationState.Completed;
        }

        if (sodiumChlorideState == StationState.Ready
            && hydrochloricAcidPour != null
            && sodiumBicarbonatePour != null
            && hydrochloricAcidPour.containsHCL
            && sodiumBicarbonatePour.containsNahco3)
        {
            sodiumChlorideState = StationState.Completed;
        }

        if (aluminumIodideState == StationState.Ready
            && iodinePour != null
            && aluminumPour != null
            && pipettePour != null
            && iodinePour.containsCuO
            && aluminumPour.containsCuO
            && pipettePour.containsWater)
        {
            aluminumIodideState = StationState.Completed;
        }

        if (calciumHydroxideState == StationState.Ready
            && calciumWaterPour != null
            && calciumOxidePour != null
            && calciumWaterPour.containsWater
            && calciumOxidePour.containsCuO
            && IsLitmusAtReactionTarget())
        {
            calciumHydroxideState = StationState.Completed;
        }
    }

    private void UpdateFailureStates()
    {
        if (copperSulfateState == StationState.Ready
            && sodiumBicarbonatePour != null
            && controlReactions != null
            && IsSolidContainerTiltedOverTarget(sodiumBicarbonatePour.Container, "pivott", controlReactions.cuso4Recipient))
        {
            FailStation(
                ref copperSulfateState,
                copperSulfateReaction,
                "Copper sulfate station failed. Sodium bicarbonate was added to the sulfuric acid setup, so the acid was contaminated and this experiment can no longer produce copper(II) sulfate.",
                controlReactions.h2so4Recipient,
                controlReactions.cuoRecipient,
                controlReactions.cuso4Recipient);
        }

        if (sodiumChlorideState == StationState.Ready
            && copperOxidePour != null
            && controlReactions != null
            && IsSolidContainerTiltedOverTarget(copperOxidePour.Container, "pivott", controlReactions.naclRecipient))
        {
            FailStation(
                ref sodiumChlorideState,
                sodiumChlorideReaction,
                "Sodium chloride station failed. Copper oxide was poured into the hydrochloric acid and sodium bicarbonate setup, so the intended salt reaction has been spoiled.",
                controlReactions.hclRecipient,
                controlReactions.nahco3Recipient,
                controlReactions.naclRecipient);
        }

        if (aluminumIodideState == StationState.Ready
            && pipettePour != null
            && pipettePour.containsWater
            && (iodinePour == null || aluminumPour == null || !iodinePour.containsCuO || !aluminumPour.containsCuO))
        {
            FailStation(
                ref aluminumIodideState,
                aluminumIodideReaction,
                "Aluminum iodide station failed. Water was added before both solids were in the crystallizing dish, so the reaction was triggered at the wrong time.",
                controlReactions.iodineRecipient,
                controlReactions.aluminumRecipient,
                controlReactions.pipette,
                controlReactions.crystallizingDish);
        }

        if (calciumHydroxideState == StationState.Ready
            && calciumHydroxideReaction != null
            && IsLitmusAtReactionTarget()
            && (calciumWaterPour == null || calciumOxidePour == null || !calciumWaterPour.containsWater || !calciumOxidePour.containsCuO))
        {
            FailStation(
                ref calciumHydroxideState,
                calciumHydroxideReaction,
                "Calcium hydroxide station failed. The litmus paper was tested before calcium hydroxide had formed, so the result is invalid.",
                controlReactions.waterRecipient4,
                controlReactions.CaO_container,
                controlReactions.TurnesolPaper,
                controlReactions.CaOH_berzelius);
        }
    }

    private void UpdateHazardWarnings()
    {
        if (aluminumSulfuricWarningShown
            || sulfuricAcidPour == null
            || aluminumPour == null
            || controlReactions == null
            || copperSulfateState == StationState.Completed
            || copperSulfateState == StationState.Failed
            || !sulfuricAcidPour.containsHCL)
        {
            return;
        }

        if (copperOxidePour != null && copperOxidePour.containsCuO)
        {
            return;
        }

        if (IsContainerNearTarget(aluminumPour.Container, "pivott", controlReactions.h2so4Recipient)
            || IsContainerNearTarget(aluminumPour.Container, "pivott", controlReactions.cuso4Recipient))
        {
            aluminumSulfuricWarningShown = true;
            ShowTransientWarning(AluminumSulfuricHazardMessage);
            Debug.Log("[LabScene2FreeModeFailureManager] Triggered aluminum + sulfuric acid hazard reminder.");
        }
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
        return Mathf.Abs(litmusPosition.x - targetPosition.x) <= litmusTestTolerance
            && Mathf.Abs(litmusPosition.z - targetPosition.z) <= litmusTestTolerance
            && Mathf.Abs(litmusPosition.y - targetPosition.y) <= 0.02f;
    }

    private bool IsSolidContainerTiltedOverTarget(GameObject source, string pivotName, GameObject target)
    {
        if (source == null || target == null || !source.activeInHierarchy || !target.activeInHierarchy)
        {
            return false;
        }

        Transform pivot = source.transform.Find(pivotName);
        if (pivot == null)
        {
            return false;
        }

        Vector3 eulerAngles = source.transform.rotation.eulerAngles;
        bool isTilted =
            (eulerAngles.x >= 45f && eulerAngles.x < 90f)
            || (eulerAngles.x <= 315f && eulerAngles.x >= 270f)
            || (eulerAngles.y >= 45f && eulerAngles.y < 90f)
            || (eulerAngles.y <= 315f && eulerAngles.y >= 270f);

        if (!isTilted)
        {
            return false;
        }

        Vector3 sourcePosition = source.transform.position;
        Vector3 targetPosition = target.transform.position;
        Vector3 pivotPosition = pivot.position;

        return sourcePosition.y > targetPosition.y
            && Mathf.Abs(pivotPosition.x - targetPosition.x) <= wrongMixTolerance
            && Mathf.Abs(pivotPosition.z - targetPosition.z) <= wrongMixTolerance;
    }

    private bool IsContainerNearTarget(GameObject source, string pivotName, GameObject target)
    {
        if (source == null || target == null || !source.activeInHierarchy || !target.activeInHierarchy)
        {
            return false;
        }

        Vector3 targetPosition = target.transform.position;
        if (IsPointNearTarget(source.transform.position, targetPosition))
        {
            return true;
        }

        Transform pivot = source.transform.Find(pivotName);
        return pivot != null && IsPointNearTarget(pivot.position, targetPosition);
    }

    private bool IsPointNearTarget(Vector3 point, Vector3 targetPosition)
    {
        Vector2 pointXZ = new Vector2(point.x, point.z);
        Vector2 targetXZ = new Vector2(targetPosition.x, targetPosition.z);
        return Vector2.Distance(pointXZ, targetXZ) <= hazardReminderHorizontalTolerance
            && Mathf.Abs(point.y - targetPosition.y) <= hazardReminderVerticalTolerance;
    }

    private void FailStation(ref StationState stationState, Behaviour reactionBehaviour, string message, params GameObject[] stationObjects)
    {
        if (stationState != StationState.Ready)
        {
            return;
        }

        stationState = StationState.Failed;
        if (reactionBehaviour != null)
        {
            reactionBehaviour.enabled = false;
        }

        foreach (GameObject stationObject in stationObjects)
        {
            if (stationObject != null)
            {
                stationObject.SetActive(false);
            }
        }

        string fullMessage = message + " Press the red reset button on the table to restore the free lab.";
        ShowSystemMessage(fullMessage);
        StationFailed?.Invoke(fullMessage);
        Debug.Log($"[LabScene2FreeModeFailureManager] {fullMessage}");
    }

    private void AnnounceAllStationsComplete()
    {
        if (allStationsAnnounced)
        {
            return;
        }

        if (copperSulfateState == StationState.Completed
            && sodiumChlorideState == StationState.Completed
            && aluminumIodideState == StationState.Completed
            && calciumHydroxideState == StationState.Completed)
        {
            allStationsAnnounced = true;
            ShowSystemMessage("All four free-lab experiments were completed successfully. You can keep exploring the lab or reopen the scene to try a different path.");
        }
    }

    private void ShowSystemMessage(string message)
    {
        LastSystemMessage = message;

        if (guidanceAudio != null)
        {
            guidanceAudio.Stop();
        }

        if (popupWindow != null)
        {
            popupWindow.SetActive(true);
        }

        if (canvasText != null)
        {
            canvasText.text = message;
        }

        SystemMessageShown?.Invoke(message);
    }

    private void ShowTransientWarning(string message)
    {
        LastSystemMessage = message;
        transientWarningMessage = message;
        transientWarningHideTime = Time.unscaledTime + Mathf.Max(1f, hazardReminderDurationSeconds);

        if (guidanceAudio != null)
        {
            guidanceAudio.Stop();
        }

        if (popupWindow != null)
        {
            popupWindow.SetActive(true);
        }

        if (canvasText != null)
        {
            canvasText.text = message;
        }

        SafetyReminderIssued?.Invoke(message);
    }

    private void ReassertTransientWarningIfActive()
    {
        if (string.IsNullOrWhiteSpace(transientWarningMessage))
        {
            return;
        }

        if (Time.unscaledTime > transientWarningHideTime)
        {
            if (canvasText != null && canvasText.text == transientWarningMessage)
            {
                canvasText.text = string.Empty;
            }

            if (popupWindow != null
                && popupWindow.activeSelf
                && canvasText != null
                && string.IsNullOrEmpty(canvasText.text))
            {
                popupWindow.SetActive(false);
            }

            transientWarningMessage = string.Empty;
            transientWarningHideTime = 0f;
            return;
        }

        if (popupWindow != null)
        {
            popupWindow.SetActive(true);
        }

        if (canvasText != null)
        {
            canvasText.text = transientWarningMessage;
        }
    }

    public void ResetState()
    {
        RefreshReferences();

        copperSulfateState = StationState.Ready;
        sodiumChlorideState = StationState.Ready;
        aluminumIodideState = StationState.Ready;
        calciumHydroxideState = StationState.Ready;
        allStationsAnnounced = false;
        aluminumSulfuricWarningShown = false;
        LastSystemMessage = string.Empty;
        transientWarningMessage = string.Empty;
        transientWarningHideTime = 0f;

        ReenableBehaviour(copperSulfateReaction);
        ReenableBehaviour(sodiumChlorideReaction);
        ReenableBehaviour(aluminumIodideReaction);
        ReenableBehaviour(calciumHydroxideReaction);

        if (guidanceAudio != null)
        {
            guidanceAudio.Stop();
        }

        if (popupWindow != null)
        {
            popupWindow.SetActive(false);
        }

        if (canvasText != null)
        {
            canvasText.text = string.Empty;
        }
    }

    private static void ReenableBehaviour(Behaviour behaviour)
    {
        if (behaviour != null)
        {
            behaviour.enabled = true;
        }
    }
}
