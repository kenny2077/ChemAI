using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Thin Azure OpenAI client for the free-lab chemistry assistant.
/// Uses the Azure OpenAI v1 endpoint shape with API-key authentication.
/// </summary>
public sealed class LabScene2AzureOpenAIClient
{
    public const string DefaultEndpoint = "https://your-resource-name.openai.azure.com/openai/v1";
    public const string DefaultAzureApiVersion = "2025-03-01-preview";
    public const string DefaultApiKey = "";
    public const string DefaultChatModel = "gpt-5";
    public const string DefaultTtsModel = "gpt-4o-mini-tts";
    public const string DefaultTranscriptionModel = "gpt-4o-mini-transcribe";
    public const string DefaultVoice = "alloy";

    private readonly string endpoint;
    private readonly string azureResourceEndpoint;
    private readonly string apiKey;
    private readonly string chatModel;
    private readonly string ttsModel;
    private readonly string transcriptionModel;
    private readonly string voice;

    public LabScene2AzureOpenAIClient(
        string endpointOverride,
        string apiKeyOverride,
        string chatModelOverride,
        string ttsModelOverride,
        string transcriptionModelOverride,
        string voiceOverride)
    {
        endpoint = string.IsNullOrWhiteSpace(endpointOverride) ? DefaultEndpoint : endpointOverride.TrimEnd('/');
        azureResourceEndpoint = DeriveAzureResourceEndpoint(endpoint);
        apiKey = string.IsNullOrWhiteSpace(apiKeyOverride) ? DefaultApiKey : apiKeyOverride;
        chatModel = string.IsNullOrWhiteSpace(chatModelOverride) ? DefaultChatModel : chatModelOverride;
        ttsModel = string.IsNullOrWhiteSpace(ttsModelOverride) ? DefaultTtsModel : ttsModelOverride;
        transcriptionModel = string.IsNullOrWhiteSpace(transcriptionModelOverride) ? DefaultTranscriptionModel : transcriptionModelOverride;
        voice = string.IsNullOrWhiteSpace(voiceOverride) ? DefaultVoice : voiceOverride;
    }

    public IEnumerator CreateAssistantResponse(
        string instructions,
        string input,
        Action<AssistantTextResponse> onSuccess,
        Action<string> onError)
    {
        ResponsesRequest payload = new ResponsesRequest
        {
            model = chatModel,
            instructions = instructions,
            input = input,
            store = false
        };

        UnityWebRequest request = BuildJsonRequest("/responses", JsonUtility.ToJson(payload));
        yield return request.SendWebRequest();

        if (!IsRequestSuccessful(request, out string errorMessage))
        {
            request.Dispose();
            onError?.Invoke(errorMessage);
            yield break;
        }

        ResponsesApiResponse response = JsonUtility.FromJson<ResponsesApiResponse>(request.downloadHandler.text);
        request.Dispose();
        string text = ExtractOutputText(response);
        if (string.IsNullOrWhiteSpace(text))
        {
            onError?.Invoke("The assistant response did not contain readable text.");
            yield break;
        }

        onSuccess?.Invoke(new AssistantTextResponse
        {
            responseId = response != null ? response.id : string.Empty,
            text = text.Trim()
        });
    }

    public IEnumerator TranscribeAudio(byte[] wavData, Action<string> onSuccess, Action<string> onError)
    {
        if (wavData == null || wavData.Length == 0)
        {
            onError?.Invoke("No microphone audio was captured.");
            yield break;
        }

        List<IMultipartFormSection> formSections = new List<IMultipartFormSection>
        {
            new MultipartFormDataSection("model", transcriptionModel),
            new MultipartFormFileSection("file", wavData, "chem-question.wav", "audio/wav")
        };

        UnityWebRequest request = UnityWebRequest.Post(
            BuildAzureDeploymentPath(transcriptionModel, "/audio/transcriptions"),
            formSections);
        request.SetRequestHeader("api-key", apiKey);
        request.SetRequestHeader("Accept", "application/json");

        yield return request.SendWebRequest();

        if (!IsRequestSuccessful(request, out string errorMessage))
        {
            request.Dispose();
            onError?.Invoke(errorMessage);
            yield break;
        }

        TranscriptionResponse response = JsonUtility.FromJson<TranscriptionResponse>(request.downloadHandler.text);
        request.Dispose();
        if (response == null || string.IsNullOrWhiteSpace(response.text))
        {
            onError?.Invoke("The transcription endpoint returned an empty transcript.");
            yield break;
        }

        onSuccess?.Invoke(response.text.Trim());
    }

    public IEnumerator SynthesizeSpeech(string text, Action<AudioClip> onSuccess, Action<string> onError)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            onError?.Invoke("Cannot synthesize empty text.");
            yield break;
        }

        SpeechRequest payload = new SpeechRequest
        {
            model = ttsModel,
            input = text,
            voice = voice,
            response_format = "wav"
        };

        UnityWebRequest request = BuildJsonRequest("/audio/speech", JsonUtility.ToJson(payload));
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Accept", "audio/wav");
        yield return request.SendWebRequest();

        if (!IsRequestSuccessful(request, out string errorMessage))
        {
            request.Dispose();
            onError?.Invoke(errorMessage);
            yield break;
        }

        AudioClip clip = LabScene2WavUtility.ToAudioClip(request.downloadHandler.data, "ChemAI_TTS");
        request.Dispose();
        if (clip == null)
        {
            onError?.Invoke("The speech endpoint returned audio that Unity could not decode.");
            yield break;
        }

        onSuccess?.Invoke(clip);
    }

    private UnityWebRequest BuildJsonRequest(string relativePath, string jsonPayload)
    {
        UnityWebRequest request = new UnityWebRequest(endpoint + relativePath, UnityWebRequest.kHttpVerbPOST);
        byte[] payloadBytes = Encoding.UTF8.GetBytes(jsonPayload);
        request.uploadHandler = new UploadHandlerRaw(payloadBytes);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        request.SetRequestHeader("Accept", "application/json");
        request.SetRequestHeader("api-key", apiKey);
        return request;
    }

    private static string DeriveAzureResourceEndpoint(string configuredEndpoint)
    {
        if (string.IsNullOrWhiteSpace(configuredEndpoint))
        {
            return string.Empty;
        }

        const string openAiV1Suffix = "/openai/v1";
        string trimmedEndpoint = configuredEndpoint.TrimEnd('/');
        int suffixIndex = trimmedEndpoint.IndexOf(openAiV1Suffix, StringComparison.OrdinalIgnoreCase);
        if (suffixIndex >= 0)
        {
            return trimmedEndpoint.Substring(0, suffixIndex);
        }

        return trimmedEndpoint;
    }

    private string BuildAzureDeploymentPath(string deploymentName, string operationPath)
    {
        string normalizedOperationPath = string.IsNullOrEmpty(operationPath)
            ? string.Empty
            : (operationPath.StartsWith("/") ? operationPath : "/" + operationPath);

        return azureResourceEndpoint
            + "/openai/deployments/"
            + UnityWebRequest.EscapeURL(deploymentName)
            + normalizedOperationPath
            + "?api-version="
            + DefaultAzureApiVersion;
    }

    private static bool IsRequestSuccessful(UnityWebRequest request, out string errorMessage)
    {
        if (request.result == UnityWebRequest.Result.Success)
        {
            errorMessage = string.Empty;
            return true;
        }

        string body = request.downloadHandler != null ? request.downloadHandler.text : string.Empty;
        string requestError = !string.IsNullOrWhiteSpace(request.error) ? request.error : "Unknown network error";
        errorMessage = requestError;

        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                ErrorEnvelope errorEnvelope = JsonUtility.FromJson<ErrorEnvelope>(body);
                if (errorEnvelope != null && errorEnvelope.error != null && !string.IsNullOrWhiteSpace(errorEnvelope.error.message))
                {
                    errorMessage = errorEnvelope.error.message;
                }
                else
                {
                    errorMessage += "\n" + body;
                }
            }
            catch
            {
                errorMessage += "\n" + body;
            }
        }

        return false;
    }

    private static string ExtractOutputText(ResponsesApiResponse response)
    {
        if (response == null)
        {
            return string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(response.output_text))
        {
            return response.output_text;
        }

        if (response.output == null)
        {
            return string.Empty;
        }

        StringBuilder builder = new StringBuilder();
        foreach (ResponsesOutputItem outputItem in response.output)
        {
            if (outputItem == null || outputItem.content == null)
            {
                continue;
            }

            foreach (ResponsesContentItem contentItem in outputItem.content)
            {
                if (contentItem != null && contentItem.type == "output_text" && !string.IsNullOrWhiteSpace(contentItem.text))
                {
                    if (builder.Length > 0)
                    {
                        builder.AppendLine();
                    }

                    builder.Append(contentItem.text);
                }
            }
        }

        return builder.ToString();
    }

    [Serializable]
    private sealed class ResponsesRequest
    {
        public string model;
        public string instructions;
        public string input;
        public bool store;
    }

    [Serializable]
    private sealed class SpeechRequest
    {
        public string model;
        public string input;
        public string voice;
        public string response_format;
    }

    [Serializable]
    private sealed class ResponsesApiResponse
    {
        public string id;
        public string output_text;
        public ResponsesOutputItem[] output;
    }

    [Serializable]
    private sealed class ResponsesOutputItem
    {
        public string type;
        public ResponsesContentItem[] content;
    }

    [Serializable]
    private sealed class ResponsesContentItem
    {
        public string type;
        public string text;
    }

    [Serializable]
    private sealed class TranscriptionResponse
    {
        public string text;
    }

    [Serializable]
    private sealed class ErrorEnvelope
    {
        public ErrorBody error;
    }

    [Serializable]
    private sealed class ErrorBody
    {
        public string message;
    }

    public sealed class AssistantTextResponse
    {
        public string responseId;
        public string text;
    }
}
