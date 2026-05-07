using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Creates a voice-first chemistry agent terminal for the free-lab scene.
/// The agent can answer student questions, speak replies, and proactively
/// offer hints based on the live experiment state summary.
/// </summary>
public class LabScene2ChemAgentManager : MonoBehaviour
{
    [System.Serializable]
    private sealed class OpenAIConfigResource
    {
        public string endpoint;
        public string apiKey;
        public string chatModel;
        public string ttsModel;
        public string transcriptionModel;
        public string voice;
    }

    private const string LocalConfigResourceName = "LabScene2OpenAIConfigLocal";

    [SerializeField] private bool enableOnStart = true;
    [SerializeField] private int waitFramesBeforeCreate = 7;
    [SerializeField] private float rayDistance = 4f;
    [SerializeField] private float touchRadius = 0.12f;
    [SerializeField] private float buttonCooldown = 0.4f;
    [SerializeField] private float maxRecordingSeconds = 10f;
    [SerializeField] private int recordingFrequency = 16000;
    [SerializeField] private int minimumRecordingMilliseconds = 500;
    [SerializeField] private float proactiveHintCooldown = 12f;
    [SerializeField] private float baseHeightOffset = 0.03f;
    [SerializeField] private float frontPlacementRatio = 0.17f;
    [SerializeField] private float horizontalOffset = -0.32f;

    [Header("Azure OpenAI")]
    [SerializeField] private string endpointOverride = LabScene2AzureOpenAIClient.DefaultEndpoint;
    [SerializeField] private string apiKeyOverride = LabScene2AzureOpenAIClient.DefaultApiKey;
    [SerializeField] private string chatModelOverride = LabScene2AzureOpenAIClient.DefaultChatModel;
    [SerializeField] private string ttsModelOverride = LabScene2AzureOpenAIClient.DefaultTtsModel;
    [SerializeField] private string transcriptionModelOverride = LabScene2AzureOpenAIClient.DefaultTranscriptionModel;
    [SerializeField] private string voiceOverride = LabScene2AzureOpenAIClient.DefaultVoice;

    private readonly Queue<ConversationTurn> conversationTurns = new Queue<ConversationTurn>();

    private LabScene2AzureOpenAIClient client;
    private ControlReactions controlReactions;
    private LabScene2ExperimentStateHub stateHub;
    private LabScene2FreeModeFailureManager failureManager;

    private GameObject terminalRoot;
    private Transform buttonCapTransform;
    private Collider buttonCapCollider;
    private Vector3 buttonCapRestLocalPosition;
    private TextMesh screenTitleText;
    private TextMesh screenStatusText;
    private TextMesh screenBodyText;
    private TextMesh buttonLabelText;
    private Renderer indicatorRenderer;
    private AudioSource speechAudioSource;

    private bool microphoneAuthorized;
    private bool isListening;
    private bool isBusy;
    private bool isSpeaking;
    private bool isSubscribedToStateHub;
    private bool isSubscribedToFailureManager;
    private float lastButtonPressTime = -10f;
    private float nextProactiveHintTime;
    private string microphoneDeviceName;
    private AudioClip recordedClip;
    private float recordingStartedAt;
    private string lastHintEvent = string.Empty;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        Scene activeScene = SceneManager.GetActiveScene();
        if (!LabScene2SceneUtility.IsFreeModeScene(activeScene))
        {
            return;
        }

        if (UnityObjectCompat.FindFirstObjectByType<LabScene2ChemAgentManager>(true) != null)
        {
            return;
        }

        GameObject host = GameObject.Find("MR_Manager");
        if (host == null)
        {
            host = new GameObject("MR_Manager");
        }

        host.AddComponent<LabScene2ChemAgentManager>();
    }

    private void Awake()
    {
        if (UnityObjectCompat.FindObjectsByType<LabScene2ChemAgentManager>(true).Length > 1)
        {
            Destroy(this);
            return;
        }

        ApplyLocalOpenAiConfigIfPresent();
        InitializeClientIfNeeded();
    }

    private IEnumerator Start()
    {
        if (!enableOnStart || !LabScene2SceneUtility.IsFreeModeScene(SceneManager.GetActiveScene()))
        {
            enabled = false;
            yield break;
        }

        int framesToWait = Mathf.Max(0, waitFramesBeforeCreate);
        for (int frame = 0; frame < framesToWait; frame++)
        {
            yield return null;
        }

        InitializeClientIfNeeded();

        RefreshReferences();
        CreateTerminalIfNeeded();
        yield return StartCoroutine(RequestMicrophonePermission());
        SetIdleMessage();
    }

    private void Update()
    {
        if (!enabled)
        {
            return;
        }

        RefreshReferences();
        CreateTerminalIfNeeded();
        EnsureSubscriptions();
        HandleButtonPress();
        HandleRecordingTimeout();
        UpdateSpeakingState();
    }

    private void OnDisable()
    {
        Unsubscribe();
        StopRecordingIfNeeded();
        StopSpeaking();
    }

    public void HandleLabResetStarted()
    {
        StopRecordingIfNeeded();
        StopSpeaking();
        isBusy = false;
        isListening = false;
        SetStatus("Resetting lab...");
        SetBodyText("Restoring all four experiment stations to their clean free-lab starting layout.");
        SetIndicatorColor(new Color(0.96f, 0.72f, 0.17f));
    }

    public void HandleLabResetCompleted()
    {
        conversationTurns.Clear();
        nextProactiveHintTime = Time.unscaledTime + 2f;
        lastHintEvent = string.Empty;
        SetIdleMessage();
    }

    private IEnumerator RequestMicrophonePermission()
    {
        if (Application.HasUserAuthorization(UserAuthorization.Microphone))
        {
            microphoneAuthorized = true;
            microphoneDeviceName = ResolveMicrophoneDevice();
            yield break;
        }

        SetStatus("Requesting mic access...");
        yield return Application.RequestUserAuthorization(UserAuthorization.Microphone);
        microphoneAuthorized = Application.HasUserAuthorization(UserAuthorization.Microphone);
        microphoneDeviceName = ResolveMicrophoneDevice();

        if (!microphoneAuthorized)
        {
            SetStatus("Mic permission denied");
            SetBodyText("Chem AI needs microphone access on the headset to hear your questions.");
            SetIndicatorColor(new Color(0.86f, 0.23f, 0.2f));
        }
    }

    private void RefreshReferences()
    {
        controlReactions ??= UnityObjectCompat.FindFirstObjectByType<ControlReactions>(true);
        stateHub ??= UnityObjectCompat.FindFirstObjectByType<LabScene2ExperimentStateHub>(true);
        failureManager ??= UnityObjectCompat.FindFirstObjectByType<LabScene2FreeModeFailureManager>(true);
    }

    private void InitializeClientIfNeeded()
    {
        client ??= new LabScene2AzureOpenAIClient(
            endpointOverride,
            apiKeyOverride,
            chatModelOverride,
            ttsModelOverride,
            transcriptionModelOverride,
            voiceOverride);
    }

    private void ApplyLocalOpenAiConfigIfPresent()
    {
        TextAsset configAsset = Resources.Load<TextAsset>(LocalConfigResourceName);
        if (configAsset == null || string.IsNullOrWhiteSpace(configAsset.text))
        {
            return;
        }

        OpenAIConfigResource config = null;
        try
        {
            config = JsonUtility.FromJson<OpenAIConfigResource>(configAsset.text);
        }
        catch
        {
            return;
        }

        if (config == null)
        {
            return;
        }

        endpointOverride = PreferConfigValue(config.endpoint, endpointOverride);
        apiKeyOverride = PreferConfigValue(config.apiKey, apiKeyOverride);
        chatModelOverride = PreferConfigValue(config.chatModel, chatModelOverride);
        ttsModelOverride = PreferConfigValue(config.ttsModel, ttsModelOverride);
        transcriptionModelOverride = PreferConfigValue(config.transcriptionModel, transcriptionModelOverride);
        voiceOverride = PreferConfigValue(config.voice, voiceOverride);
    }

    private static string PreferConfigValue(string configuredValue, string fallbackValue)
    {
        return string.IsNullOrWhiteSpace(configuredValue) ? fallbackValue : configuredValue.Trim();
    }

    private void EnsureSubscriptions()
    {
        if (!isSubscribedToStateHub && stateHub != null)
        {
            stateHub.SnapshotChanged += OnStateSnapshotChanged;
            isSubscribedToStateHub = true;
        }

        if (!isSubscribedToFailureManager && failureManager != null)
        {
            failureManager.StationFailed += OnStationFailed;
            failureManager.SafetyReminderIssued += OnSafetyReminderIssued;
            isSubscribedToFailureManager = true;
        }
    }

    private void Unsubscribe()
    {
        if (isSubscribedToStateHub && stateHub != null)
        {
            stateHub.SnapshotChanged -= OnStateSnapshotChanged;
            isSubscribedToStateHub = false;
        }

        if (isSubscribedToFailureManager && failureManager != null)
        {
            failureManager.StationFailed -= OnStationFailed;
            failureManager.SafetyReminderIssued -= OnSafetyReminderIssued;
            isSubscribedToFailureManager = false;
        }
    }

    private void CreateTerminalIfNeeded()
    {
        if (terminalRoot != null || controlReactions == null)
        {
            return;
        }

        Vector3 terminalPosition = ResolveTerminalPosition();
        terminalRoot = new GameObject("LabScene2_ChemAgentTerminal");
        terminalRoot.transform.position = terminalPosition;
        terminalRoot.transform.rotation = Quaternion.Euler(0f, 180f, 0f);

        GameObject pedestal = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        pedestal.name = "Pedestal";
        pedestal.transform.SetParent(terminalRoot.transform, false);
        pedestal.transform.localPosition = Vector3.zero;
        pedestal.transform.localScale = new Vector3(0.14f, 0.02f, 0.14f);
        TintObject(pedestal, new Color(0.12f, 0.14f, 0.18f));

        GameObject stem = GameObject.CreatePrimitive(PrimitiveType.Cube);
        stem.name = "Stem";
        stem.transform.SetParent(terminalRoot.transform, false);
        stem.transform.localPosition = new Vector3(0f, 0.12f, 0.045f);
        stem.transform.localScale = new Vector3(0.03f, 0.18f, 0.03f);
        TintObject(stem, new Color(0.16f, 0.18f, 0.22f));

        GameObject screen = GameObject.CreatePrimitive(PrimitiveType.Cube);
        screen.name = "Screen";
        screen.transform.SetParent(terminalRoot.transform, false);
        screen.transform.localPosition = new Vector3(0f, 0.22f, 0.055f);
        screen.transform.localScale = new Vector3(0.36f, 0.14f, 0.03f);
        TintObject(screen, new Color(0.08f, 0.1f, 0.12f));

        screenTitleText = CreateTextMesh("TitleText", terminalRoot.transform, new Vector3(0f, 0.3f, 0.037f), 0.018f, 44, Color.cyan);
        screenStatusText = CreateTextMesh("StatusText", terminalRoot.transform, new Vector3(0f, 0.245f, 0.037f), 0.012f, 34, Color.white);
        screenTitleText.text = "CHEM AI";

        GameObject indicator = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        indicator.name = "Indicator";
        indicator.transform.SetParent(terminalRoot.transform, false);
        indicator.transform.localPosition = new Vector3(0.14f, 0.3f, 0.04f);
        indicator.transform.localScale = Vector3.one * 0.03f;
        indicatorRenderer = indicator.GetComponent<Renderer>();

        GameObject buttonBase = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        buttonBase.name = "ButtonBase";
        buttonBase.transform.SetParent(terminalRoot.transform, false);
        buttonBase.transform.localPosition = new Vector3(0f, 0.038f, -0.07f);
        buttonBase.transform.localScale = new Vector3(0.11f, 0.018f, 0.11f);
        TintObject(buttonBase, new Color(0.13f, 0.15f, 0.18f));

        GameObject buttonCap = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        buttonCap.name = "ButtonCap";
        buttonCap.transform.SetParent(terminalRoot.transform, false);
        buttonCap.transform.localPosition = new Vector3(0f, 0.065f, -0.07f);
        buttonCap.transform.localScale = new Vector3(0.075f, 0.012f, 0.075f);
        TintObject(buttonCap, new Color(0.08f, 0.55f, 0.82f));
        buttonCapTransform = buttonCap.transform;
        buttonCapCollider = buttonCap.GetComponent<Collider>();
        buttonCapRestLocalPosition = buttonCapTransform.localPosition;

        buttonLabelText = CreateTextMesh("ButtonText", terminalRoot.transform, new Vector3(0f, 0.105f, -0.07f), 0.0105f, 28, Color.white);
        UpdateButtonLabel();

        speechAudioSource = terminalRoot.AddComponent<AudioSource>();
        speechAudioSource.playOnAwake = false;
        speechAudioSource.spatialBlend = 0.35f;
        speechAudioSource.minDistance = 0.6f;
        speechAudioSource.maxDistance = 8f;

        CreateLargeResponsePanel();
        SetIndicatorColor(new Color(0.11f, 0.65f, 0.38f));
    }

    private void CreateLargeResponsePanel()
    {
        const float panelWidth = 0.86f;
        const float panelHeight = 0.58f;
        const float panelDepth = 0.024f;

        ResolveResponsePanelPose(panelWidth, out Vector3 panelPosition, out Quaternion panelRotation);

        GameObject responsePanel = new GameObject("LabScene2_ChemAgentResponsePanel");
        responsePanel.transform.position = panelPosition;
        responsePanel.transform.rotation = panelRotation;

        GameObject backdrop = GameObject.CreatePrimitive(PrimitiveType.Cube);
        backdrop.name = "Backdrop";
        backdrop.transform.SetParent(responsePanel.transform, false);
        backdrop.transform.localPosition = Vector3.zero;
        backdrop.transform.localScale = new Vector3(panelWidth, panelHeight, panelDepth);
        TintObject(backdrop, new Color(0.07f, 0.09f, 0.11f));
        Collider backdropCollider = backdrop.GetComponent<Collider>();
        if (backdropCollider != null)
        {
            backdropCollider.enabled = false;
        }

        TextMesh responseTitleText = CreateTextMesh(
            "ResponseTitleText",
            responsePanel.transform,
            new Vector3(0f, (panelHeight * 0.5f) - 0.06f, (panelDepth * 0.5f) + 0.001f),
            0.0125f,
            36,
            Color.cyan);
        responseTitleText.text = "CHEM AI RESPONSE";

        screenBodyText = CreateTextMesh(
            "BodyText",
            responsePanel.transform,
            new Vector3(-(panelWidth * 0.5f) + 0.055f, (panelHeight * 0.5f) - 0.12f, (panelDepth * 0.5f) + 0.0015f),
            0.0094f,
            28,
            new Color(0.95f, 0.97f, 1f));
        screenBodyText.anchor = TextAnchor.UpperLeft;
        screenBodyText.alignment = TextAlignment.Left;
    }

    private void ResolveResponsePanelPose(float panelWidth, out Vector3 position, out Quaternion rotation)
    {
        const float fallbackHeightOffset = 0.22f;
        const float fallbackSideOffset = 0.52f;
        const float popupGap = 0.1f;

        RectTransform popupRect = null;
        if (controlReactions != null && controlReactions.canvasText != null)
        {
            popupRect = controlReactions.canvasText.rectTransform.parent as RectTransform;
        }

        if (popupRect == null && controlReactions != null && controlReactions.popupWindow != null)
        {
            popupRect = controlReactions.popupWindow.GetComponent<RectTransform>();
        }

        if (popupRect != null)
        {
            rotation = popupRect.rotation;
            float popupWorldWidth = Mathf.Clamp(popupRect.rect.width * popupRect.lossyScale.x, 0.18f, 0.8f);
            float horizontalOffset = (popupWorldWidth * 0.5f) + (panelWidth * 0.5f) + popupGap;
            position = popupRect.position + (popupRect.right * horizontalOffset) + (popupRect.up * 0.01f);
            return;
        }

        rotation = terminalRoot != null ? terminalRoot.transform.rotation : Quaternion.identity;
        Vector3 right = terminalRoot != null ? terminalRoot.transform.right : Vector3.right;
        Vector3 up = terminalRoot != null ? terminalRoot.transform.up : Vector3.up;
        position = (terminalRoot != null ? terminalRoot.transform.position : Vector3.zero)
            + (right * fallbackSideOffset)
            + (up * fallbackHeightOffset);
    }

    private Vector3 ResolveTerminalPosition()
    {
        GameObject[] anchors =
        {
            controlReactions.cuso4Recipient,
            controlReactions.naclRecipient,
            controlReactions.crystallizingDish,
            controlReactions.CaOH_berzelius
        };

        float minX = float.MaxValue;
        float maxX = float.MinValue;
        float minZ = float.MaxValue;
        float maxZ = float.MinValue;
        float averageY = 0f;
        int count = 0;

        foreach (GameObject anchor in anchors)
        {
            if (anchor == null)
            {
                continue;
            }

            Vector3 position = anchor.transform.position;
            minX = Mathf.Min(minX, position.x);
            maxX = Mathf.Max(maxX, position.x);
            minZ = Mathf.Min(minZ, position.z);
            maxZ = Mathf.Max(maxZ, position.z);
            averageY += position.y;
            count++;
        }

        if (count == 0)
        {
            return Vector3.zero;
        }

        averageY /= count;

        Vector3 candidate = new Vector3(
            (minX + maxX) * 0.5f + horizontalOffset,
            averageY + 1.5f,
            Mathf.Lerp(minZ, maxZ, frontPlacementRatio));

        if (Physics.Raycast(candidate, Vector3.down, out RaycastHit hit, 3f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            candidate = hit.point;
        }
        else
        {
            candidate = new Vector3(candidate.x, averageY, candidate.z);
        }

        candidate.y += baseHeightOffset;
        return candidate;
    }

    private void HandleButtonPress()
    {
        if (buttonCapCollider == null || Time.unscaledTime - lastButtonPressTime < buttonCooldown)
        {
            return;
        }

        if (!LabScene2ControllerRayUtility.TryGetTriggeredController(out OVRInput.Controller controller))
        {
            return;
        }

        if (!IsButtonHit(controller))
        {
            return;
        }

        lastButtonPressTime = Time.unscaledTime;
        StartCoroutine(AnimateButtonPress());

        if (isBusy && !isListening)
        {
            return;
        }

        if (!microphoneAuthorized)
        {
            StartCoroutine(RequestMicrophonePermission());
            return;
        }

        if (!isListening)
        {
            BeginListening();
        }
        else
        {
            StopListeningAndSend();
        }
    }

    private bool IsButtonHit(OVRInput.Controller controller)
    {
        if (!LabScene2ControllerRayUtility.TryGetWorldPose(controller, out Vector3 controllerPosition, out Quaternion _))
        {
            return false;
        }

        if (LabScene2ControllerRayUtility.TryGetWorldRay(controller, out Ray ray)
            && buttonCapCollider.Raycast(ray, out RaycastHit _, rayDistance))
        {
            return true;
        }

        Vector3 closestPoint = buttonCapCollider.ClosestPoint(controllerPosition);
        return (closestPoint - controllerPosition).sqrMagnitude <= touchRadius * touchRadius;
    }

    private void BeginListening()
    {
        microphoneDeviceName = ResolveMicrophoneDevice();
        if (string.IsNullOrEmpty(microphoneDeviceName))
        {
            SetStatus("No microphone");
            SetBodyText("Quest microphone input was not found. Check headset microphone permission and device availability.");
            SetIndicatorColor(new Color(0.86f, 0.23f, 0.2f));
            return;
        }

        StopSpeaking();
        ReleaseRecordedClip();
        recordedClip = Microphone.Start(microphoneDeviceName, false, Mathf.CeilToInt(maxRecordingSeconds), recordingFrequency);
        if (recordedClip == null)
        {
            SetStatus("Mic start failed");
            SetBodyText("Chem AI could not start recording from the headset microphone.");
            SetIndicatorColor(new Color(0.86f, 0.23f, 0.2f));
            return;
        }

        isListening = true;
        recordingStartedAt = Time.unscaledTime;
        SetStatus("Listening... press again to send");
        SetBodyText("Ask Chem AI a question about the experiment, safety, reagent choice, or what to do next.");
        SetIndicatorColor(new Color(0.16f, 0.68f, 0.96f));
        UpdateButtonLabel();
    }

    private void StopListeningAndSend()
    {
        if (!isListening)
        {
            return;
        }

        int sampleFrames = Microphone.GetPosition(microphoneDeviceName);
        Microphone.End(microphoneDeviceName);
        isListening = false;
        UpdateButtonLabel();

        int minimumFrames = Mathf.RoundToInt(recordingFrequency * (minimumRecordingMilliseconds / 1000f));
        if (recordedClip == null || sampleFrames < minimumFrames)
        {
            ReleaseRecordedClip();
            SetStatus("No question captured");
            SetBodyText("I did not hear enough audio. Press ASK again and speak for a little longer.");
            SetIndicatorColor(new Color(0.96f, 0.72f, 0.17f));
            return;
        }

        byte[] wavData = LabScene2WavUtility.FromAudioClip(recordedClip, sampleFrames);
        ReleaseRecordedClip();
        StartCoroutine(ProcessVoiceQuestion(wavData));
    }

    private void HandleRecordingTimeout()
    {
        if (!isListening)
        {
            return;
        }

        if (Time.unscaledTime - recordingStartedAt >= maxRecordingSeconds)
        {
            StopListeningAndSend();
        }
    }

    private IEnumerator ProcessVoiceQuestion(byte[] wavData)
    {
        isBusy = true;
        SetStatus("Transcribing question...");
        SetIndicatorColor(new Color(0.96f, 0.72f, 0.17f));

        string transcript = null;
        string error = null;
        yield return client.TranscribeAudio(wavData, result => transcript = result, resultError => error = resultError);
        if (!string.IsNullOrWhiteSpace(error))
        {
            ShowError("Transcription failed", error);
            isBusy = false;
            yield break;
        }

        if (string.IsNullOrWhiteSpace(transcript))
        {
            ShowError("Empty transcript", "I could not turn the microphone audio into text.");
            isBusy = false;
            yield break;
        }

        SetStatus("Thinking...");
        SetBodyText("You asked:\n" + transcript);

        string answer = null;
        bool useLocalStateReply = ShouldAnswerFromLocalState(transcript);
        EnqueueTurn("Student", transcript);

        if (!useLocalStateReply)
        {
            string input = BuildQuestionInput(transcript);
            LabScene2AzureOpenAIClient.AssistantTextResponse assistantResponse = null;
            error = null;
            yield return client.CreateAssistantResponse(BuildChatInstructions(), input, result => assistantResponse = result, resultError => error = resultError);
            if (!string.IsNullOrWhiteSpace(error))
            {
                ShowError("Chem AI request failed", error);
                isBusy = false;
                yield break;
            }

            answer = assistantResponse != null ? assistantResponse.text : string.Empty;
        }

        if (useLocalStateReply || IsGenericAssistantRefusal(answer))
        {
            answer = BuildQuestionFallback(transcript);
        }

        if (string.IsNullOrWhiteSpace(answer))
        {
            ShowError("Empty answer", "The chemistry assistant returned an empty answer.");
            isBusy = false;
            yield break;
        }

        EnqueueTurn("Chem AI", answer);
        SetStatus("Chem AI answering");
        SetBodyText("You asked:\n" + transcript + "\n\nChem AI:\n" + answer);

        yield return StartCoroutine(SpeakText(answer));
        isBusy = false;
        SetIdleReadyState();
    }

    private IEnumerator SpeakText(string text)
    {
        if (speechAudioSource == null)
        {
            yield break;
        }

        InitializeClientIfNeeded();
        if (client == null)
        {
            ShowError("Speech unavailable", "Chem AI speech is not ready yet.");
            yield break;
        }

        StopSpeaking();
        isSpeaking = true;

        AudioClip clip = null;
        string error = null;
        yield return client.SynthesizeSpeech(text, result => clip = result, resultError => error = resultError);
        if (!string.IsNullOrWhiteSpace(error))
        {
            ShowError("Speech synthesis failed", error);
            isSpeaking = false;
            yield break;
        }

        if (clip == null)
        {
            isSpeaking = false;
            yield break;
        }

        if (controlReactions != null && controlReactions.audioSource_guidance != null)
        {
            controlReactions.audioSource_guidance.Stop();
        }

        speechAudioSource.clip = clip;
        speechAudioSource.Play();
        SetIndicatorColor(new Color(0.16f, 0.68f, 0.96f));
    }

    private void UpdateSpeakingState()
    {
        if (!isSpeaking || speechAudioSource == null || speechAudioSource.isPlaying)
        {
            return;
        }

        isSpeaking = false;
        SetIdleReadyState();
    }

    private void StopSpeaking()
    {
        isSpeaking = false;
        if (speechAudioSource != null)
        {
            speechAudioSource.Stop();
            if (speechAudioSource.clip != null)
            {
                Destroy(speechAudioSource.clip);
                speechAudioSource.clip = null;
            }
        }
    }

    private void StopRecordingIfNeeded()
    {
        if (!isListening)
        {
            return;
        }

        Microphone.End(microphoneDeviceName);
        isListening = false;
        UpdateButtonLabel();
        ReleaseRecordedClip();
    }

    private void OnStateSnapshotChanged(LabScene2ExperimentStateHub.ExperimentStateSnapshot snapshot)
    {
        if (snapshot == null || !ShouldRequestProactiveHint(snapshot))
        {
            return;
        }

        lastHintEvent = snapshot.focusEvent;
        StartCoroutine(RequestProactiveHint(snapshot));
    }

    private void OnStationFailed(string failureMessage)
    {
        if (string.IsNullOrWhiteSpace(failureMessage) || isListening || isBusy)
        {
            return;
        }

        nextProactiveHintTime = Time.unscaledTime + proactiveHintCooldown;
        SetStatus("Safety warning");
        SetBodyText("Chem AI warning:\n" + failureMessage);
        SetIndicatorColor(new Color(0.86f, 0.23f, 0.2f));
        StartCoroutine(SpeakText(failureMessage));
    }

    private void OnSafetyReminderIssued(string reminderMessage)
    {
        if (string.IsNullOrWhiteSpace(reminderMessage))
        {
            return;
        }

        nextProactiveHintTime = Time.unscaledTime + proactiveHintCooldown;
        SetStatus("Safety reminder");
        SetBodyText("Chem AI warning:\n" + reminderMessage);
        SetIndicatorColor(new Color(0.86f, 0.23f, 0.2f));

        if (!isListening && !isBusy)
        {
            StartCoroutine(SpeakText(reminderMessage));
        }
    }

    private IEnumerator RequestProactiveHint(LabScene2ExperimentStateHub.ExperimentStateSnapshot snapshot)
    {
        if (isBusy || isListening)
        {
            yield break;
        }

        isBusy = true;
        SetStatus("Reviewing experiment state...");
        SetIndicatorColor(new Color(0.96f, 0.72f, 0.17f));

        string hint = BuildProactiveHint(snapshot);
        isBusy = false;
        if (string.IsNullOrWhiteSpace(hint) || hint.Trim().ToUpperInvariant().StartsWith("NO_HINT"))
        {
            SetIdleReadyState();
            yield break;
        }

        nextProactiveHintTime = Time.unscaledTime + proactiveHintCooldown;
        SetStatus(snapshot.hasRiskOrFailure ? "Safety reminder" : "Chem AI hint");
        SetBodyText(hint);
        SetIndicatorColor(snapshot.hasRiskOrFailure ? new Color(0.86f, 0.23f, 0.2f) : new Color(0.15f, 0.7f, 0.42f));
        yield return StartCoroutine(SpeakText(hint));
    }

    private bool ShouldRequestProactiveHint(LabScene2ExperimentStateHub.ExperimentStateSnapshot snapshot)
    {
        if (snapshot == null
            || !snapshot.hasInterestingChange
            || string.IsNullOrWhiteSpace(snapshot.focusEvent)
            || snapshot.focusEvent == lastHintEvent
            || Time.unscaledTime < nextProactiveHintTime
            || isListening
            || isBusy
            || isSpeaking)
        {
            return false;
        }

        string summary = snapshot.summary != null ? snapshot.summary.ToLowerInvariant() : string.Empty;
        return summary.Contains("in progress")
            || summary.Contains("failed")
            || summary.Contains("completed");
    }

    private string BuildQuestionInput(string userQuestion)
    {
        StringBuilder builder = new StringBuilder();
        builder.AppendLine("Student question:");
        builder.AppendLine(userQuestion.Trim());
        builder.AppendLine();

        if (stateHub != null)
        {
            builder.AppendLine(stateHub.BuildStateSummary());
            builder.AppendLine();
        }

        if (conversationTurns.Count > 0)
        {
            builder.AppendLine("Recent conversation:");
            builder.AppendLine(BuildRecentConversationContext());
        }

        return builder.ToString().Trim();
    }

    private static bool ShouldAnswerFromLocalState(string userQuestion)
    {
        if (string.IsNullOrWhiteSpace(userQuestion))
        {
            return false;
        }

        string normalizedQuestion = userQuestion.Trim().ToLowerInvariant();
        return normalizedQuestion.Contains("next")
            || normalizedQuestion.Contains("what should i do")
            || normalizedQuestion.Contains("what do i do")
            || normalizedQuestion.Contains("what now")
            || normalizedQuestion.Contains("what is missing")
            || normalizedQuestion.Contains("what should i add")
            || normalizedQuestion.Contains("which reagent")
            || normalizedQuestion.Contains("safe")
            || normalizedQuestion.Contains("danger")
            || normalizedQuestion.Contains("failed")
            || normalizedQuestion.Contains("wrong")
            || normalizedQuestion.Contains("contamin")
            || normalizedQuestion.Contains("what happened")
            || normalizedQuestion.Contains("what is happening")
            || normalizedQuestion.Contains("what's happening")
            || normalizedQuestion.Contains("status")
            || normalizedQuestion.Contains("summary");
    }

    private string BuildQuestionFallback(string userQuestion)
    {
        LabScene2ExperimentStateHub.ExperimentStateSnapshot snapshot = stateHub != null ? stateHub.GetLatestSnapshot() : null;
        if (snapshot == null)
        {
            return "I can describe the simulation once the live lab state is available.";
        }

        string normalizedQuestion = string.IsNullOrWhiteSpace(userQuestion)
            ? string.Empty
            : userQuestion.Trim().ToLowerInvariant();

        if (snapshot.hasRiskOrFailure)
        {
            return BuildFailureOrSafetyReply(snapshot);
        }

        if (normalizedQuestion.Contains("next")
            || normalizedQuestion.Contains("what should i do")
            || normalizedQuestion.Contains("what do i do")
            || normalizedQuestion.Contains("what now")
            || normalizedQuestion.Contains("what is missing")
            || normalizedQuestion.Contains("what should i add")
            || normalizedQuestion.Contains("which reagent"))
        {
            return BuildNextStepReply(snapshot);
        }

        if (normalizedQuestion.Contains("safe")
            || normalizedQuestion.Contains("danger")
            || normalizedQuestion.Contains("failed")
            || normalizedQuestion.Contains("wrong")
            || normalizedQuestion.Contains("contamin"))
        {
            return BuildFailureOrSafetyReply(snapshot);
        }

        return BuildStateNarration(snapshot);
    }

    private static string BuildChatInstructions()
    {
        return
            "You are Chem AI for a supervised classroom VR chemistry simulation, not a real lab workflow assistant. "
            + "Always ground your answer in the provided simulation state summary. If the state summary is missing a detail, say that you are inferring it. "
            + "Be concise, practical, and safety-aware. You may describe the current simulated state, expected observation, contamination or reset status, and the next missing in-game reagent or tool. "
            + "Do not invent quantities, timings, temperatures, or procedural details beyond the specific simulated objects and steps already present in the summary. "
            + "If there is a contamination, failure, or dangerous misuse, say so clearly and tell the student to reset that station. "
            + "Respond in the same language the student uses if it is clear from the question.";
    }

    private static bool IsGenericAssistantRefusal(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string normalized = text.Trim().ToLowerInvariant();
        return normalized.StartsWith("i'm sorry")
            || normalized.StartsWith("i am sorry")
            || normalized.Contains("cannot assist with that request")
            || normalized.Contains("can't assist with that request")
            || normalized.Contains("cannot help with that request")
            || normalized.Contains("can't help with that request");
    }

    private static string BuildProactiveHint(LabScene2ExperimentStateHub.ExperimentStateSnapshot snapshot)
    {
        if (snapshot == null)
        {
            return string.Empty;
        }

        if (snapshot.hasRiskOrFailure)
        {
            return BuildFailureOrSafetyReply(snapshot);
        }

        return BuildNextStepReply(snapshot);
    }

    private static string BuildStateNarration(LabScene2ExperimentStateHub.ExperimentStateSnapshot snapshot)
    {
        if (snapshot == null)
        {
            return string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(snapshot.focusEvent))
        {
            return "Based on the live simulation state, " + LowercaseFirst(snapshot.focusEvent);
        }

        return snapshot.summary;
    }

    private static string BuildFailureOrSafetyReply(LabScene2ExperimentStateHub.ExperimentStateSnapshot snapshot)
    {
        if (snapshot == null)
        {
            return string.Empty;
        }

        string focusEvent = snapshot.focusEvent ?? string.Empty;
        if (focusEvent.IndexOf("failed state", System.StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "That station has failed or been contaminated in the simulation. Reset it before continuing.";
        }

        return "Be careful with the current station state. If a station is contaminated or failed, reset it before adding anything else.";
    }

    private static string BuildNextStepReply(LabScene2ExperimentStateHub.ExperimentStateSnapshot snapshot)
    {
        if (snapshot == null)
        {
            return string.Empty;
        }

        string focusEvent = snapshot.focusEvent ?? string.Empty;
        if (focusEvent.IndexOf("waiting for copper oxide", System.StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "In the simulation, add copper oxide to the copper sulfate station next.";
        }

        if (focusEvent.IndexOf("waiting for sulfuric acid", System.StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "In the simulation, add sulfuric acid to the copper sulfate station next.";
        }

        if (focusEvent.IndexOf("waiting for sodium bicarbonate", System.StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "In the simulation, add sodium bicarbonate to the sodium chloride station next.";
        }

        if (focusEvent.IndexOf("waiting for hydrochloric acid", System.StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "In the simulation, add hydrochloric acid to the sodium chloride station next.";
        }

        if (focusEvent.IndexOf("waiting for pipette water", System.StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "In the simulation, fill the pipette and add water to the aluminum iodide station next.";
        }

        if (focusEvent.IndexOf("waiting for iodine", System.StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "In the simulation, add iodine to the aluminum iodide station next.";
        }

        if (focusEvent.IndexOf("waiting for aluminum", System.StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "In the simulation, add aluminum to the aluminum iodide station next.";
        }

        if (focusEvent.IndexOf("waiting for the litmus test", System.StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "In the simulation, test the calcium hydroxide station with the red litmus paper next.";
        }

        if (focusEvent.IndexOf("waiting for calcium oxide", System.StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "In the simulation, add calcium oxide to the calcium hydroxide station next.";
        }

        if (focusEvent.IndexOf("waiting for water", System.StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "In the simulation, add water to the calcium hydroxide station next.";
        }

        if (focusEvent.IndexOf("has just completed", System.StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "That station is complete. Move to another station or ask about the reaction result.";
        }

        if (focusEvent.IndexOf("failed state", System.StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return BuildFailureOrSafetyReply(snapshot);
        }

        return BuildStateNarration(snapshot);
    }

    private static string LowercaseFirst(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        string trimmed = text.Trim();
        if (trimmed.Length == 1)
        {
            return trimmed.ToLowerInvariant();
        }

        return char.ToLowerInvariant(trimmed[0]) + trimmed.Substring(1);
    }

    private void EnqueueTurn(string speaker, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        conversationTurns.Enqueue(new ConversationTurn { speaker = speaker, text = text.Trim() });
        while (conversationTurns.Count > 6)
        {
            conversationTurns.Dequeue();
        }
    }

    private string BuildRecentConversationContext()
    {
        if (conversationTurns.Count == 0)
        {
            return string.Empty;
        }

        StringBuilder builder = new StringBuilder();
        foreach (ConversationTurn turn in conversationTurns)
        {
            builder.AppendLine(turn.speaker + ": " + turn.text);
        }

        return builder.ToString().Trim();
    }

    private static string ResolveMicrophoneDevice()
    {
        return Microphone.devices != null && Microphone.devices.Length > 0
            ? Microphone.devices[0]
            : string.Empty;
    }

    private void ReleaseRecordedClip()
    {
        if (recordedClip == null)
        {
            return;
        }

        Destroy(recordedClip);
        recordedClip = null;
    }

    private IEnumerator AnimateButtonPress()
    {
        if (buttonCapTransform == null)
        {
            yield break;
        }

        buttonCapTransform.localPosition = buttonCapRestLocalPosition + Vector3.down * 0.012f;
        yield return new WaitForSecondsRealtime(0.12f);
        buttonCapTransform.localPosition = buttonCapRestLocalPosition;
    }

    private void SetIdleMessage()
    {
        SetStatus(microphoneAuthorized ? "Ready for questions" : "Mic permission required");
        SetBodyText(
            microphoneAuthorized
                ? "Press ASK once to start speaking and press it again to send your question. Chem AI can answer questions and warn you about the current experiment state."
                : "Chem AI needs headset microphone permission before it can hear your questions.");
        SetIdleReadyState();
    }

    private void SetIdleReadyState()
    {
        if (isListening || isBusy || isSpeaking)
        {
            return;
        }

        SetStatus(microphoneAuthorized ? "Ready for questions" : "Mic permission required");
        SetIndicatorColor(microphoneAuthorized ? new Color(0.11f, 0.65f, 0.38f) : new Color(0.86f, 0.23f, 0.2f));
        UpdateButtonLabel();
    }

    private void ShowError(string title, string details)
    {
        SetStatus(title);
        SetBodyText(details);
        SetIndicatorColor(new Color(0.86f, 0.23f, 0.2f));
    }

    private void SetStatus(string text)
    {
        if (screenStatusText != null)
        {
            screenStatusText.text = text;
        }
    }

    private void SetBodyText(string text)
    {
        if (screenBodyText != null)
        {
            screenBodyText.text = WrapText(text, 60, 20);
        }
    }

    private void UpdateButtonLabel()
    {
        if (buttonLabelText != null)
        {
            buttonLabelText.text = isListening ? "SEND" : "ASK";
        }
    }

    private void SetIndicatorColor(Color color)
    {
        if (indicatorRenderer != null)
        {
            indicatorRenderer.material.color = color;
            if (indicatorRenderer.material.HasProperty("_BaseColor"))
            {
                indicatorRenderer.material.SetColor("_BaseColor", color);
            }
        }
    }

    private static TextMesh CreateTextMesh(string name, Transform parent, Vector3 localPosition, float characterSize, int fontSize, Color color)
    {
        GameObject textObject = new GameObject(name);
        textObject.transform.SetParent(parent, false);
        textObject.transform.localPosition = localPosition;
        textObject.transform.localRotation = Quaternion.identity;

        TextMesh textMesh = textObject.AddComponent<TextMesh>();
        textMesh.anchor = TextAnchor.MiddleCenter;
        textMesh.alignment = TextAlignment.Center;
        textMesh.characterSize = characterSize;
        textMesh.fontSize = fontSize;
        textMesh.color = color;
        return textMesh;
    }

    private static string WrapText(string input, int maxCharactersPerLine, int maxLines)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        string[] words = input.Replace("\r", string.Empty).Replace("\n", " \n ").Split(' ');
        StringBuilder builder = new StringBuilder();
        int currentLineLength = 0;
        int currentLineCount = 1;

        foreach (string rawWord in words)
        {
            string word = rawWord;
            if (word == "\n")
            {
                if (currentLineCount >= maxLines)
                {
                    break;
                }

                builder.AppendLine();
                currentLineCount++;
                currentLineLength = 0;
                continue;
            }

            if (string.IsNullOrWhiteSpace(word))
            {
                continue;
            }

            while (word.Length > maxCharactersPerLine)
            {
                int availableCharacters = currentLineLength == 0
                    ? maxCharactersPerLine
                    : maxCharactersPerLine - currentLineLength - 1;

                if (availableCharacters <= 0)
                {
                    if (currentLineCount >= maxLines)
                    {
                        builder.Append("...");
                        return builder.ToString().Trim();
                    }

                    builder.AppendLine();
                    currentLineCount++;
                    currentLineLength = 0;
                    continue;
                }

                if (currentLineLength > 0)
                {
                    builder.Append(' ');
                    currentLineLength++;
                }

                builder.Append(word.Substring(0, availableCharacters));
                word = word.Substring(availableCharacters);
                currentLineLength += availableCharacters;

                if (currentLineCount >= maxLines)
                {
                    builder.Append("...");
                    return builder.ToString().Trim();
                }

                builder.AppendLine();
                currentLineCount++;
                currentLineLength = 0;
            }

            int projectedLength = currentLineLength == 0 ? word.Length : currentLineLength + 1 + word.Length;
            if (projectedLength > maxCharactersPerLine)
            {
                if (currentLineCount >= maxLines)
                {
                    builder.Append("...");
                    break;
                }

                builder.AppendLine();
                currentLineCount++;
                currentLineLength = 0;
            }
            else if (currentLineLength > 0)
            {
                builder.Append(' ');
                currentLineLength++;
            }

            builder.Append(word);
            currentLineLength += word.Length;
        }

        return builder.ToString().Trim();
    }

    private static void TintObject(GameObject sceneObject, Color color)
    {
        if (sceneObject == null)
        {
            return;
        }

        Renderer renderer = sceneObject.GetComponent<Renderer>();
        if (renderer == null)
        {
            return;
        }

        Material material = renderer.material;
        material.color = color;
        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", color);
        }
    }

    private sealed class ConversationTurn
    {
        public string speaker;
        public string text;
    }
}
