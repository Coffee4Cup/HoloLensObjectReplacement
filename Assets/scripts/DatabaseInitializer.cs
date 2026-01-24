using UnityEngine;

public class DatabaseInitializer : MonoBehaviour
{
    void Start()
    {
        ModelDatabase db = GetComponent<ModelDatabase>();

        // Method 1: If prefabs are directly in Models/
        AddModelIfExists(db, "Models/WashingMachine1", "Samsung Front Load");
        AddModelIfExists(db, "Models/WashingMachine2", "LG Top Load");

        // Method 2: If prefabs are in subfolders like Models/WM1/WM1.prefab
        // AddModelIfExists(db, "Models/WashingMachine1/WashingMachine1", "Samsung Front Load");
        // AddModelIfExists(db, "Models/WashingMachine2/WashingMachine2", "LG Top Load");
    }

    void AddModelIfExists(ModelDatabase db, string path, string displayName)
    {
        GameObject model = Resources.Load<GameObject>(path);
        if (model != null)
        {
            db.AddModel(model, displayName, "appliance_large");
            Debug.Log($"Added {displayName} from path: {path}");
        }
        else
        {
            Debug.LogError($"Could not find model at path: {path}");
        }
    }
}