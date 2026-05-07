using UnityEngine.SceneManagement;

public static class LabScene2SceneUtility
{
    public const string GuidedSceneName = "LabScene 2";
    public const string FreeModeSceneName = "LabScene 2 Free";

    public static bool IsLabScene2Variant(Scene scene)
    {
        return IsLabScene2Variant(scene.name);
    }

    public static bool IsLabScene2Variant(string sceneName)
    {
        return sceneName == GuidedSceneName || sceneName == FreeModeSceneName;
    }

    public static bool IsFreeModeScene(Scene scene)
    {
        return IsFreeModeScene(scene.name);
    }

    public static bool IsFreeModeScene(string sceneName)
    {
        return sceneName == FreeModeSceneName;
    }
}
