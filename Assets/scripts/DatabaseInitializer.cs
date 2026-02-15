using UnityEngine;

/// <summary>
/// Automatically discovers and registers all model prefabs in Resources/Models/
/// No need to manually list each model - just drop prefabs into the folder.
/// </summary>
public class DatabaseInitializer : MonoBehaviour
{
    [Header("Settings")]
    [SerializeField] private string modelsResourcePath = "Models";
    [SerializeField] private string defaultCategory = "appliance_large";
    [SerializeField] private bool forceReExtractFeatures = false;

    void Start()
    {
        ModelDatabase db = GetComponent<ModelDatabase>();
        if (db == null)
        {
            Debug.LogError("DatabaseInitializer: No ModelDatabase component found!");
            return;
        }

        // Only rebuild if database is empty or force re-extract is enabled
        if (db.GetModelCount() > 0 && !forceReExtractFeatures)
        {
            Debug.Log($"Database already has {db.GetModelCount()} entries, skipping initialization. " +
                      "Enable 'Force Re-Extract Features' in Inspector to rebuild.");
            return;
        }

        if (forceReExtractFeatures)
        {
            Debug.Log("Force re-extract enabled - clearing and rebuilding database");
            db.ClearDatabase();
        }

        // Auto-discover ALL prefabs in the Resources/Models/ folder
        GameObject[] allModels = Resources.LoadAll<GameObject>(modelsResourcePath);

        if (allModels.Length == 0)
        {
            Debug.LogWarning($"No models found in Resources/{modelsResourcePath}/. " +
                             "Drop prefabs into that folder to register them.");
            return;
        }

        Debug.Log($"Found {allModels.Length} models in Resources/{modelsResourcePath}/");

        int added = 0;
        int skipped = 0;

        foreach (GameObject model in allModels)
        {
            // Check if the mesh is readable
            MeshFilter mf = model.GetComponentInChildren<MeshFilter>();
            if (mf == null || mf.sharedMesh == null)
            {
                Debug.LogWarning($"Skipping '{model.name}' - no MeshFilter found");
                skipped++;
                continue;
            }

            if (!mf.sharedMesh.isReadable)
            {
                Debug.LogError($"Mesh on '{model.name}' is not readable! " +
                               "Select the source model in Project window -> Inspector -> Model tab -> " +
                               "enable 'Read/Write' -> Apply");
                skipped++;
                continue;
            }

            // Use the prefab name as the display name, and build the resource path
            string displayName = FormatDisplayName(model.name);
            string resourcePath = modelsResourcePath + "/" + model.name;

            db.AddModel(model, displayName, defaultCategory);
            Debug.Log($"Added '{displayName}' from path: {resourcePath}");
            added++;
        }

        Debug.Log($"Database initialization complete: {added} models added, {skipped} skipped");
    }

    /// <summary>
    /// Convert prefab name to a readable display name
    /// e.g. "washingMachine1" -> "Washing Machine 1"
    /// e.g. "LG_TopLoad_Washer" -> "LG Top Load Washer"
    /// </summary>
    private string FormatDisplayName(string prefabName)
    {
        string result = "";

        for (int i = 0; i < prefabName.Length; i++)
        {
            char c = prefabName[i];

            // Replace underscores and hyphens with spaces
            if (c == '_' || c == '-')
            {
                result += " ";
                continue;
            }

            // Add space before uppercase letters (camelCase splitting)
            if (i > 0 && char.IsUpper(c) && !char.IsUpper(prefabName[i - 1]))
            {
                result += " ";
            }

            // Add space before numbers if preceded by a letter
            if (i > 0 && char.IsDigit(c) && char.IsLetter(prefabName[i - 1]))
            {
                result += " ";
            }

            result += c;
        }

        // Capitalize first letter
        if (result.Length > 0)
        {
            result = char.ToUpper(result[0]) + result.Substring(1);
        }

        return result.Trim();
    }
}
