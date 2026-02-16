using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Initializes the model database with washing machine prefabs.
/// Clears old DB on start during development to ensure fresh features.
/// Also registers scene model objects with the scanner's exclude list.
/// </summary>
public class DatabaseInitializer : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private HoloLensSpatialScanner scanner;

    [Header("Scene Model Objects (drag from Hierarchy)")]
    [Tooltip("Drag the scene instances of washing machines here so scanner auto-excludes them")]
    [SerializeField] private List<GameObject> sceneModelObjects = new List<GameObject>();

    void Start()
    {
        ModelDatabase db = GetComponent<ModelDatabase>();
        if (db == null)
        {
            Debug.LogError("DatabaseInitializer: No ModelDatabase found on this GameObject!");
            return;
        }

        // Clear old database during development to force re-extraction of features
        db.ClearDatabase();
        db.InitializeDatabase();

        // Add models - these paths must match your Resources/Models/ folder
        // The prefab name in Resources must match exactly (case sensitive)
        AddModelIfExists(db, "Models/washingMachine1", "Washing Machine 1");
        AddModelIfExists(db, "Models/WashingMachine2", "Washing Machine 2");
        AddModelIfExists(db, "Models/washingMachine3", "Washing Machine 3");

        Debug.Log($"Database initialized with {db.GetModelCount()} models");

        // Register scene objects with scanner for exclusion
        if (scanner == null) scanner = FindObjectOfType<HoloLensSpatialScanner>();
        if (scanner != null)
        {
            foreach (GameObject sceneObj in sceneModelObjects)
            {
                if (sceneObj != null) scanner.ExcludeObject(sceneObj);
            }
        }
    }

    void AddModelIfExists(ModelDatabase db, string path, string displayName)
    {
        GameObject model = Resources.Load<GameObject>(path);
        if (model != null)
        {
            db.AddModel(model, displayName, "appliance_large");
            Debug.Log($"[DB Init] Added '{displayName}' from '{path}'");
        }
        else
        {
            Debug.LogError($"[DB Init] Could not find model at: Resources/{path}");
        }
    }
}
