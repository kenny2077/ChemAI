using UnityEngine;

public static class UnityObjectCompat
{
    public static T FindFirstObjectByType<T>(bool includeInactive = false) where T : Object
    {
#if UNITY_2023_1_OR_NEWER
        return includeInactive
            ? Object.FindFirstObjectByType<T>(FindObjectsInactive.Include)
            : Object.FindFirstObjectByType<T>();
#else
        return Object.FindObjectOfType<T>(includeInactive);
#endif
    }

    public static T[] FindObjectsByType<T>(bool includeInactive = false) where T : Object
    {
#if UNITY_2023_1_OR_NEWER
        return includeInactive
            ? Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            : Object.FindObjectsByType<T>(FindObjectsSortMode.None);
#else
        return Object.FindObjectsOfType<T>(includeInactive);
#endif
    }
}
