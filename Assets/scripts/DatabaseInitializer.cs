using Unity.VisualScripting.Antlr3.Runtime.Tree;
using UnityEngine;

public class DatabaseInitializer : MonoBehaviour
{
    void Start()
    {
        ModelDatabase db = GetComponent<ModelDatabase>();

        // Load test model from Resources
        GameObject testModel = Resources.Load<GameObject>("Models/TestModel");

        if (testModel != null)
        {
            db.AddModel(testModel, "Test Washing Machine", "appliance_large");
            Debug.Log("Added test model to database!");
        }
        else
        {
            Debug.LogError("Could not find TestModel in Resources/Models/");
        }
    }
}