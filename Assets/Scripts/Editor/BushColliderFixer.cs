using UnityEditor;
using UnityEngine;

/// <summary>
/// One-shot tool to make all bush MeshColliders convex so the player Rigidbody
/// can't walk through them. Run via Tools > Fix Bush Colliders.
/// </summary>
public static class BushColliderFixer
{
    [MenuItem("Tools/Fix Bush Colliders")]
    static void FixBushColliders()
    {
        // Find every GameObject tagged "Bush" across all loaded scenes.
        GameObject[] bushObjects = GameObject.FindGameObjectsWithTag("Bush");

        if (bushObjects.Length == 0)
        {
            Debug.LogWarning("BushColliderFixer: No GameObjects with tag 'Bush' found. " +
                             "Make sure the scene is open and the tag is assigned.");
            return;
        }

        int fixedCount    = 0;
        int removedCount  = 0;

        foreach (GameObject go in bushObjects)
        {
            MeshCollider[] colliders = go.GetComponents<MeshCollider>();

            MeshCollider first = null;

            foreach (MeshCollider mc in colliders)
            {
                // Keep the first collider that has a valid mesh; remove duplicates.
                if (mc.sharedMesh == null)
                {
                    Undo.DestroyObjectImmediate(mc);
                    removedCount++;
                    continue;
                }

                if (first == null)
                {
                    if (!mc.convex)
                    {
                        Undo.RecordObject(mc, "Make Bush Collider Convex");
                        mc.convex = true;
                        fixedCount++;
                    }
                    first = mc;
                }
                else
                {
                    // Duplicate collider — remove it.
                    Undo.DestroyObjectImmediate(mc);
                    removedCount++;
                }
            }
        }

        // Also remove the null-mesh parent colliders (LODGroup parents).
        string[] guids = AssetDatabase.FindAssets("t:Scene", new[] { "Assets/Scenes" });
        // Only fix GameObjects in the currently loaded scenes (already in memory).
        GameObject[] allObjects = Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None);
        foreach (GameObject go in allObjects)
        {
            if (go.name.StartsWith("Bush_") && !go.CompareTag("Bush"))
            {
                MeshCollider mc = go.GetComponent<MeshCollider>();
                if (mc != null && mc.sharedMesh == null)
                {
                    Undo.DestroyObjectImmediate(mc);
                    removedCount++;
                }
            }
        }

        UnityEditor.SceneManagement.EditorSceneManager.MarkAllScenesDirty();

        Debug.Log($"BushColliderFixer: Made {fixedCount} colliders convex, " +
                  $"removed {removedCount} null/duplicate colliders. " +
                  $"Save the scene to persist changes.");
    }
}
