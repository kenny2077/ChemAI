using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Spreads the four free-lab chemistry stations into separate table corners so
/// their equipment no longer overlaps when every experiment is enabled at once.
/// </summary>
public class LabScene2FreeModeLayoutManager : MonoBehaviour
{
    [SerializeField] private bool enableOnStart = true;
    [SerializeField] private int waitFramesBeforeLayout = 2;
    [SerializeField] private float cornerOffsetX = 0.42f;
    [SerializeField] private float cornerOffsetZ = 0.28f;
    [SerializeField] private float sheetInsetDistance = 0.18f;
    [SerializeField] private float sheetLift = 0.01f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        Scene activeScene = SceneManager.GetActiveScene();
        if (!LabScene2SceneUtility.IsFreeModeScene(activeScene))
        {
            return;
        }

        if (UnityObjectCompat.FindFirstObjectByType<LabScene2FreeModeLayoutManager>(true) != null)
        {
            return;
        }

        GameObject host = GameObject.Find("MR_Manager");
        if (host == null)
        {
            host = new GameObject("MR_Manager");
        }

        host.AddComponent<LabScene2FreeModeLayoutManager>();
    }

    private void Awake()
    {
        if (UnityObjectCompat.FindObjectsByType<LabScene2FreeModeLayoutManager>(true).Length > 1)
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

        int framesToWait = Mathf.Max(0, waitFramesBeforeLayout);
        for (int frame = 0; frame < framesToWait; frame++)
        {
            yield return null;
        }

        LayoutStations();
    }

    private void LayoutStations()
    {
        ControlReactions controlReactions = UnityObjectCompat.FindFirstObjectByType<ControlReactions>(true);
        if (controlReactions == null)
        {
            Debug.LogWarning("[LabScene2FreeModeLayoutManager] ControlReactions not found.");
            return;
        }

        StationGroup[] allStations = BuildStationGroups(controlReactions);
        List<StationGroup> stations = new List<StationGroup>(allStations.Length);
        Vector3 layoutCenter = Vector3.zero;

        foreach (StationGroup station in allStations)
        {
            if (station.AnchorObject == null)
            {
                Debug.LogWarning("[LabScene2FreeModeLayoutManager] Skipping a station with no anchor object.");
                continue;
            }

            stations.Add(station);
            layoutCenter += station.AnchorObject.transform.position;
        }

        if (stations.Count == 0)
        {
            Debug.LogWarning("[LabScene2FreeModeLayoutManager] No station anchors were available to lay out.");
            return;
        }

        layoutCenter /= stations.Count;

        Vector3[] cornerOffsets =
        {
            new Vector3(-cornerOffsetX, 0f, -cornerOffsetZ),
            new Vector3(cornerOffsetX, 0f, -cornerOffsetZ),
            new Vector3(-cornerOffsetX, 0f, cornerOffsetZ),
            new Vector3(cornerOffsetX, 0f, cornerOffsetZ)
        };

        int stationsToMove = Mathf.Min(stations.Count, cornerOffsets.Length);
        for (int index = 0; index < stationsToMove; index++)
        {
            Vector3 targetAnchorPosition = layoutCenter + cornerOffsets[index];
            targetAnchorPosition.y = stations[index].AnchorObject.transform.position.y;
            MoveStation(stations[index], targetAnchorPosition, layoutCenter);
        }

        Debug.Log("[LabScene2FreeModeLayoutManager] Positioned the four free-mode chemistry stations into separate corners.");
    }

    private StationGroup[] BuildStationGroups(ControlReactions controlReactions)
    {
        Reaction_hcl_nahco3 sodiumChlorideReaction = UnityObjectCompat.FindFirstObjectByType<Reaction_hcl_nahco3>(true);
        ReactionAli3 aluminumIodideReaction = UnityObjectCompat.FindFirstObjectByType<ReactionAli3>(true);
        reactionCaOH calciumHydroxideReaction = UnityObjectCompat.FindFirstObjectByType<reactionCaOH>(true);

        return new[]
        {
            new StationGroup(
                "Copper sulfate",
                controlReactions.cuso4Recipient,
                controlReactions.experimentSheet2,
                controlReactions.h2so4Recipient,
                controlReactions.cuoRecipient,
                controlReactions.cuso4Recipient,
                controlReactions.experimentSheet2),
            new StationGroup(
                "Sodium chloride",
                controlReactions.naclRecipient,
                controlReactions.experimentSheet3,
                controlReactions.hclRecipient,
                controlReactions.nahco3Recipient,
                controlReactions.naclRecipient,
                controlReactions.experimentSheet3,
                sodiumChlorideReaction != null ? sodiumChlorideReaction.explosionGameObject : null),
            new StationGroup(
                "Aluminum iodide",
                controlReactions.crystallizingDish,
                controlReactions.experimentSheet5,
                controlReactions.waterRecipient3,
                controlReactions.iodineRecipient,
                controlReactions.aluminumRecipient,
                controlReactions.pipette,
                controlReactions.crystallizingDish,
                controlReactions.experimentSheet5,
                aluminumIodideReaction != null ? aluminumIodideReaction.explosionGameObject1 : null,
                aluminumIodideReaction != null ? aluminumIodideReaction.explosionGameObject2 : null,
                aluminumIodideReaction != null ? aluminumIodideReaction.explosionGameObject3 : null,
                aluminumIodideReaction != null ? aluminumIodideReaction.firstPowder : null,
                aluminumIodideReaction != null ? aluminumIodideReaction.blackPowder : null,
                aluminumIodideReaction != null ? aluminumIodideReaction.purplePowder : null,
                aluminumIodideReaction != null ? aluminumIodideReaction.whitePowder : null),
            new StationGroup(
                "Calcium hydroxide",
                controlReactions.CaOH_berzelius,
                controlReactions.experimentSheet6,
                controlReactions.waterRecipient4,
                controlReactions.CaO_container,
                controlReactions.CaOH_berzelius,
                controlReactions.TurnesolPaper,
                controlReactions.experimentSheet6,
                calciumHydroxideReaction != null ? calciumHydroxideReaction.CaOHPivot : null,
                calciumHydroxideReaction != null ? calciumHydroxideReaction.TurnesolPivot : null,
                calciumHydroxideReaction != null ? calciumHydroxideReaction.wetLitmusPaper : null,
                calciumHydroxideReaction != null ? calciumHydroxideReaction.wetLitmusPaper1 : null,
                calciumHydroxideReaction != null ? calciumHydroxideReaction.explosionGameObject : null)
        };
    }

    private void MoveStation(StationGroup station, Vector3 targetAnchorPosition, Vector3 layoutCenter)
    {
        Vector3 delta = targetAnchorPosition - station.AnchorObject.transform.position;
        foreach (Transform root in CollectStationRoots(station.Objects))
        {
            TranslateRoot(root, delta);
        }

        PositionSheet(station.SheetObject, station.AnchorObject.transform.position, layoutCenter);
        Debug.Log("[LabScene2FreeModeLayoutManager] Moved " + station.Name + " station into place.");
    }

    private void PositionSheet(GameObject sheetObject, Vector3 anchorPosition, Vector3 layoutCenter)
    {
        if (sheetObject == null)
        {
            return;
        }

        Vector3 towardCenter = layoutCenter - anchorPosition;
        towardCenter.y = 0f;
        if (towardCenter.sqrMagnitude < 0.0001f)
        {
            towardCenter = Vector3.back;
        }

        Vector3 sheetTarget = anchorPosition + towardCenter.normalized * sheetInsetDistance + Vector3.up * sheetLift;
        Rigidbody body = sheetObject.GetComponent<Rigidbody>();
        if (body != null)
        {
            body.position = sheetTarget;
            ResetBodyMotion(body);
        }
        else
        {
            sheetObject.transform.position = sheetTarget;
        }
    }

    private static List<Transform> CollectStationRoots(GameObject[] objects)
    {
        HashSet<Transform> candidates = new HashSet<Transform>();
        foreach (GameObject sceneObject in objects)
        {
            if (sceneObject != null)
            {
                candidates.Add(sceneObject.transform);
            }
        }

        List<Transform> roots = new List<Transform>(candidates.Count);
        foreach (Transform candidate in candidates)
        {
            Transform parent = candidate.parent;
            bool isChildOfAnotherStationObject = false;

            while (parent != null)
            {
                if (candidates.Contains(parent))
                {
                    isChildOfAnotherStationObject = true;
                    break;
                }

                parent = parent.parent;
            }

            if (!isChildOfAnotherStationObject)
            {
                roots.Add(candidate);
            }
        }

        return roots;
    }

    private static void TranslateRoot(Transform root, Vector3 delta)
    {
        if (root == null || delta == Vector3.zero)
        {
            return;
        }

        Rigidbody body = root.GetComponent<Rigidbody>();
        if (body != null)
        {
            body.position += delta;
            ResetBodyMotion(body);
        }
        else
        {
            root.position += delta;
        }
    }

    private static void ResetBodyMotion(Rigidbody body)
    {
        if (body == null)
        {
            return;
        }

#if UNITY_6000_0_OR_NEWER
        body.linearVelocity = Vector3.zero;
#else
        body.velocity = Vector3.zero;
#endif
        body.angularVelocity = Vector3.zero;
    }

    private sealed class StationGroup
    {
        public StationGroup(string name, GameObject anchorObject, GameObject sheetObject, params GameObject[] objects)
        {
            Name = name;
            AnchorObject = anchorObject;
            SheetObject = sheetObject;
            Objects = objects;
        }

        public string Name { get; }
        public GameObject AnchorObject { get; }
        public GameObject SheetObject { get; }
        public GameObject[] Objects { get; }
    }
}
