using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Creates a free-play version of LabScene 2 where four chemistry stations are
/// available at the same time and the legacy book path is disabled.
/// </summary>
public class LabScene2FreeModeManager : MonoBehaviour
{
    [SerializeField] private bool enableOnStart = true;
    [SerializeField] private bool enableExperimentSheets = true;
    [SerializeField] private bool hideBook = true;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        Scene activeScene = SceneManager.GetActiveScene();
        if (!LabScene2SceneUtility.IsFreeModeScene(activeScene))
        {
            return;
        }

        if (UnityObjectCompat.FindFirstObjectByType<LabScene2FreeModeManager>(true) != null)
        {
            return;
        }

        GameObject host = GameObject.Find("MR_Manager");
        if (host == null)
        {
            host = new GameObject("MR_Manager");
        }

        host.AddComponent<LabScene2FreeModeManager>();
    }

    private void Awake()
    {
        if (UnityObjectCompat.FindObjectsByType<LabScene2FreeModeManager>(true).Length > 1)
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

        // Let the duplicated scene finish its normal Start() calls first,
        // then switch it into free mode in a single pass.
        yield return null;
        ConfigureFreeMode();
    }

    private void ConfigureFreeMode()
    {
        ControlReactions controlReactions = UnityObjectCompat.FindFirstObjectByType<ControlReactions>(true);
        if (controlReactions == null)
        {
            Debug.LogWarning("[LabScene2FreeModeManager] ControlReactions not found.");
            return;
        }

        SetReactionObjects(controlReactions);
        SetExperimentSheets(controlReactions);
        ShowIntro(controlReactions.canvasText, controlReactions.popupWindow, controlReactions.audioSource_guidance);
        DisablePresetFlow(controlReactions);

        Debug.Log("[LabScene2FreeModeManager] Enabled free-order chemistry mode with reactions 2, 3, 5, and 6 active.");
    }

    public void RestoreFreeModeStartState()
    {
        if (!LabScene2SceneUtility.IsFreeModeScene(SceneManager.GetActiveScene()))
        {
            return;
        }

        ConfigureFreeMode();
    }

    private void SetReactionObjects(ControlReactions controlReactions)
    {
        SetObjectsActive(
            true,
            controlReactions.h2so4Recipient,
            controlReactions.cuoRecipient,
            controlReactions.cuso4Recipient,
            controlReactions.hclRecipient,
            controlReactions.nahco3Recipient,
            controlReactions.naclRecipient,
            controlReactions.waterRecipient3,
            controlReactions.iodineRecipient,
            controlReactions.aluminumRecipient,
            controlReactions.pipette,
            controlReactions.crystallizingDish,
            controlReactions.waterRecipient4,
            controlReactions.CaO_container,
            controlReactions.CaOH_berzelius,
            controlReactions.TurnesolPaper);

        SetObjectsActive(
            false,
            controlReactions.waterRecipient1,
            controlReactions.natriumRecipient,
            controlReactions.newNatriumRecipient,
            controlReactions.phenolphthaleinRecipient,
            controlReactions.naohRecipient,
            controlReactions.waterRecipient2,
            controlReactions.potassiumRecipient,
            controlReactions.newPotassiumRecipient,
            controlReactions.methylorangeRecipient,
            controlReactions.kohRecipient,
            controlReactions.bunsenBurner1,
            controlReactions.testTubeSuport1,
            controlReactions.balloon,
            controlReactions.testTube1,
            controlReactions.bunsenBurner2,
            controlReactions.testTubeSuport2,
            controlReactions.testTube2);
    }

    private void SetExperimentSheets(ControlReactions controlReactions)
    {
        SetObjectsActive(
            enableExperimentSheets,
            controlReactions.experimentSheet2,
            controlReactions.experimentSheet3,
            controlReactions.experimentSheet5,
            controlReactions.experimentSheet6);

        SetObjectsActive(
            false,
            controlReactions.experimentSheet1,
            controlReactions.experimentSheet4,
            controlReactions.experimentSheet7,
            controlReactions.experimentSheet8);
    }

    private void ShowIntro(TMP_Text canvasText, GameObject popupWindow, AudioSource guidanceAudio)
    {
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
            canvasText.text =
                "Free lab mode: four chemistry stations are ready in the four corners of the table. "
                + "You can complete them in any order. "
                + "If you want to restart, press the red reset button on the table. "
                + "If you want live help, use the Chem AI terminal on the table to ask a question. "
                + "AI updates will appear here as each reaction progresses:\n"
                + "1. Sulfuric acid + copper oxide\n"
                + "2. Hydrochloric acid + sodium bicarbonate\n"
                + "3. Aluminum + iodine, then add water with the pipette\n"
                + "4. Calcium oxide + water, then test with red litmus paper";
        }
    }

    private void DisablePresetFlow(ControlReactions controlReactions)
    {
        DisableIfPresent(controlReactions);
        DisableIfPresent(UnityObjectCompat.FindFirstObjectByType<FlipPages>(true));
        DisableIfPresent(UnityObjectCompat.FindFirstObjectByType<BookCanvasManager>(true));
        DisableIfPresent(UnityObjectCompat.FindFirstObjectByType<LabScene2BookInteractionFixer>(true));
        DisableIfPresent(UnityObjectCompat.FindFirstObjectByType<LabScene2BookUiInteractionFixer>(true));
        DisableIfPresent(UnityObjectCompat.FindFirstObjectByType<LabScene2ControllerPointerFixer>(true));
        DisableIfPresent(UnityObjectCompat.FindFirstObjectByType<LabScene2ControllerInputDebug>(true));
        HideLegacyPointerObjects();

        if (!hideBook)
        {
            return;
        }

        BookCanvasManager bookCanvasManager = UnityObjectCompat.FindFirstObjectByType<BookCanvasManager>(true);
        if (bookCanvasManager != null && bookCanvasManager.bookCanvas != null)
        {
            bookCanvasManager.bookCanvas.SetActive(false);
        }

        foreach (Transform sceneTransform in UnityObjectCompat.FindObjectsByType<Transform>(true))
        {
            if (sceneTransform == null)
            {
                continue;
            }

            if (sceneTransform.name == "Book" || sceneTransform.name.StartsWith("Book ("))
            {
                sceneTransform.gameObject.SetActive(false);
            }
        }
    }

    private static void HideLegacyPointerObjects()
    {
        foreach (Transform sceneTransform in UnityObjectCompat.FindObjectsByType<Transform>(true))
        {
            if (sceneTransform == null)
            {
                continue;
            }

            string name = sceneTransform.name;
            if (name.StartsWith("LabScene2_") && (name.EndsWith("_Beam") || name.EndsWith("_Reticle")))
            {
                sceneTransform.gameObject.SetActive(false);
            }
        }
    }

    private static void DisableIfPresent(Behaviour behaviour)
    {
        if (behaviour != null)
        {
            behaviour.enabled = false;
        }
    }

    private static void SetObjectsActive(bool active, params GameObject[] objects)
    {
        foreach (GameObject sceneObject in objects)
        {
            if (sceneObject != null)
            {
                sceneObject.SetActive(active);
            }
        }
    }
}
