using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System;

/// <summary>
/// Serializable key-value pair to replace Dictionary (JsonUtility can't serialize Dictionary)
/// </summary>
[System.Serializable]
public class SerializableKeyValue
{
    public string key;
    public string value;

    public SerializableKeyValue() { }

    public SerializableKeyValue(string key, string value)
    {
        this.key = key;
        this.value = value;
    }
}

/// <summary>
/// Database entry for a 3D model
/// MRTK3 Compatible
/// </summary>
[System.Serializable]
public class ModelEntry
{
    public string id;
    public string name;
    public string category;
    public string modelPath; // Path to the 3D model file
    public string thumbnailPath;
    public MeshFeatures features;
    public string dateAdded; // Changed from DateTime (not serializable by JsonUtility)

    // Metadata - using serializable list instead of Dictionary
    public List<SerializableKeyValue> metadata;

    public ModelEntry()
    {
        id = Guid.NewGuid().ToString();
        dateAdded = DateTime.Now.ToString("o"); // ISO 8601 format
        metadata = new List<SerializableKeyValue>();
    }

    /// <summary>
    /// Helper: Get metadata value by key
    /// </summary>
    public string GetMetadata(string key)
    {
        var entry = metadata.Find(m => m.key == key);
        return entry != null ? entry.value : null;
    }

    /// <summary>
    /// Helper: Set metadata value by key
    /// </summary>
    public void SetMetadata(string key, string value)
    {
        var entry = metadata.Find(m => m.key == key);
        if (entry != null)
        {
            entry.value = value;
        }
        else
        {
            metadata.Add(new SerializableKeyValue(key, value));
        }
    }
}

/// <summary>
/// Search result with similarity score
/// </summary>
public class SearchResult
{
    public ModelEntry model;
    public float similarityScore;
    public string matchReason;

    public SearchResult(ModelEntry model, float score, string reason = "")
    {
        this.model = model;
        this.similarityScore = score;
        this.matchReason = reason;
    }
}

/// <summary>
/// Manages a database of 3D models with feature-based search
/// </summary>
public class ModelDatabase : MonoBehaviour
{
    [Header("Database Settings")]
    [SerializeField] private string databasePath = "ModelDatabase";
    [SerializeField] private string modelsFolder = "Models";
    [SerializeField] private int maxSearchResults = 10;

    private List<ModelEntry> database = new List<ModelEntry>();
    private MeshFeatureExtractor featureExtractor;
    private bool isInitialized = false;

    void Awake()
    {
        featureExtractor = GetComponent<MeshFeatureExtractor>();
        if (featureExtractor == null)
        {
            featureExtractor = gameObject.AddComponent<MeshFeatureExtractor>();
        }

        InitializeDatabase();
    }

    /// <summary>
    /// Initialize database from saved data or create new
    /// </summary>
    public void InitializeDatabase()
    {
        string dbFile = Path.Combine(Application.persistentDataPath, databasePath, "database.json");

        if (File.Exists(dbFile))
        {
            LoadDatabase(dbFile);
        }
        else
        {
            Debug.Log("No existing database found, creating new database");
            database = new List<ModelEntry>();
        }

        isInitialized = true;
        Debug.Log($"Database initialized with {database.Count} entries");
    }

    /// <summary>
    /// Add a new model to the database
    /// </summary>
    public void AddModel(GameObject modelObject, string name, string category, Dictionary<string, string> metadataDict = null)
    {
        if (!isInitialized)
        {
            Debug.LogError("Database not initialized!");
            return;
        }

        MeshFilter meshFilter = modelObject.GetComponentInChildren<MeshFilter>();
        if (meshFilter == null || meshFilter.sharedMesh == null)
        {
            Debug.LogError("Model has no mesh!");
            return;
        }

        ModelEntry entry = new ModelEntry
        {
            name = name,
            category = category,
            features = featureExtractor.ExtractFeatures(meshFilter.sharedMesh),
            modelPath = "Models/" + modelObject.name
        };

        // Convert dictionary to serializable list
        if (metadataDict != null)
        {
            foreach (var kvp in metadataDict)
            {
                entry.metadata.Add(new SerializableKeyValue(kvp.Key, kvp.Value));
            }
        }

        database.Add(entry);

        Debug.Log($"Added model '{name}' to database (ID: {entry.id}) with path: {entry.modelPath}");

        SaveDatabase();
    }

    /// <summary>
    /// Search for similar models based on features
    /// </summary>
    public List<SearchResult> SearchSimilar(MeshFeatures queryFeatures, int topK = -1)
    {
        if (!isInitialized)
        {
            Debug.LogError("Database not initialized!");
            return new List<SearchResult>();
        }

        if (topK < 0) topK = maxSearchResults;

        List<SearchResult> results = new List<SearchResult>();

        // First pass: filter by category if available
        List<ModelEntry> candidates = database;
        if (!string.IsNullOrEmpty(queryFeatures.suggestedCategory))
        {
            var categoryMatches = database.Where(e => e.category == queryFeatures.suggestedCategory).ToList();
            if (categoryMatches.Count > 0)
            {
                candidates = categoryMatches;
                Debug.Log($"Filtered to {candidates.Count} models in category '{queryFeatures.suggestedCategory}'");
            }
        }

        // Calculate similarity scores
        foreach (ModelEntry entry in candidates)
        {
            if (entry.features == null)
            {
                Debug.LogWarning($"Skipping model '{entry.name}' - no features extracted");
                continue;
            }

            float score = featureExtractor.CompareFeaturesSimple(queryFeatures, entry.features);
            string reason = BuildMatchReason(queryFeatures, entry.features, score);
            results.Add(new SearchResult(entry, score, reason));
        }

        // Sort by similarity score (descending)
        results = results.OrderByDescending(r => r.similarityScore).Take(topK).ToList();

        Debug.Log($"Found {results.Count} similar models, top score: {(results.Count > 0 ? results[0].similarityScore : 0):F3}");

        return results;
    }

    /// <summary>
    /// Search by specific criteria
    /// </summary>
    public List<SearchResult> SearchByDimensions(Vector3 size, float tolerance = 0.2f)
    {
        List<SearchResult> results = new List<SearchResult>();

        foreach (ModelEntry entry in database)
        {
            if (entry.features == null) continue;

            Vector3 entrySize = entry.features.boundingBoxSize;

            // Check if dimensions are within tolerance
            float maxDiff = 0f;

            maxDiff = Mathf.Max(maxDiff, Mathf.Abs(entrySize.x - size.x) / size.x);
            maxDiff = Mathf.Max(maxDiff, Mathf.Abs(entrySize.y - size.y) / size.y);
            maxDiff = Mathf.Max(maxDiff, Mathf.Abs(entrySize.z - size.z) / size.z);

            if (maxDiff <= tolerance)
            {
                float score = 1f - (maxDiff / tolerance);
                results.Add(new SearchResult(entry, score, $"Dimensions match within {maxDiff * 100:F1}%"));
            }
        }

        return results.OrderByDescending(r => r.similarityScore).ToList();
    }

    /// <summary>
    /// Build a human-readable match reason
    /// </summary>
    private string BuildMatchReason(MeshFeatures query, MeshFeatures candidate, float score)
    {
        List<string> reasons = new List<string>();

        // Check aspect ratios
        if (Mathf.Abs(query.aspectRatioXY - candidate.aspectRatioXY) < 0.2f)
        {
            reasons.Add("similar proportions");
        }

        // Check compactness
        if (Mathf.Abs(query.compactness - candidate.compactness) < 0.2f)
        {
            reasons.Add("similar shape compactness");
        }

        // Check category
        if (query.suggestedCategory == candidate.suggestedCategory)
        {
            reasons.Add($"same category ({query.suggestedCategory})");
        }

        if (reasons.Count == 0)
        {
            return $"Overall similarity: {score:F2}";
        }

        return string.Join(", ", reasons) + $" (score: {score:F2})";
    }

    /// <summary>
    /// Load a model from the database by ID
    /// </summary>
    public GameObject LoadModel(string modelId)
    {
        ModelEntry entry = database.FirstOrDefault(e => e.id == modelId);
        if (entry == null)
        {
            Debug.LogError($"Model with ID {modelId} not found");
            return null;
        }

        // Load the model from Resources
        GameObject model = Resources.Load<GameObject>(entry.modelPath);

        if (model == null)
        {
            Debug.LogError($"Failed to load model from path: {entry.modelPath}");
            return null;
        }

        Debug.Log($"Successfully loaded model: {entry.name} from {entry.modelPath}");
        return Instantiate(model);
    }

    /// <summary>
    /// Save database to JSON file
    /// </summary>
    private void SaveDatabase()
    {
        string dbFolder = Path.Combine(Application.persistentDataPath, databasePath);
        if (!Directory.Exists(dbFolder))
        {
            Directory.CreateDirectory(dbFolder);
        }

        string dbFile = Path.Combine(dbFolder, "database.json");

        try
        {
            DatabaseWrapper wrapper = new DatabaseWrapper { entries = database };
            string json = JsonUtility.ToJson(wrapper, true);
            File.WriteAllText(dbFile, json);
            Debug.Log($"Database saved to {dbFile}");
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to save database: {e.Message}");
        }
    }

    /// <summary>
    /// Load database from JSON file
    /// </summary>
    private void LoadDatabase(string filePath)
    {
        try
        {
            string json = File.ReadAllText(filePath);
            DatabaseWrapper wrapper = JsonUtility.FromJson<DatabaseWrapper>(json);
            if (wrapper != null && wrapper.entries != null)
            {
                database = wrapper.entries;
                Debug.Log($"Database loaded from {filePath} with {database.Count} entries");
            }
            else
            {
                Debug.LogWarning("Database file was empty or malformed, starting fresh");
                database = new List<ModelEntry>();
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to load database: {e.Message}");
            database = new List<ModelEntry>();
        }
    }

    /// <summary>
    /// Get all models in a category
    /// </summary>
    public List<ModelEntry> GetByCategory(string category)
    {
        return database.Where(e => e.category == category).ToList();
    }

    /// <summary>
    /// Get total number of models in database
    /// </summary>
    public int GetModelCount()
    {
        return database.Count;
    }

    /// <summary>
    /// Clear all entries from the database and delete the saved file
    /// </summary>
    public void ClearDatabase()
    {
        database.Clear();

        string dbFile = Path.Combine(Application.persistentDataPath, databasePath, "database.json");
        if (File.Exists(dbFile))
        {
            File.Delete(dbFile);
            Debug.Log("Database file deleted");
        }

        Debug.Log("Database cleared");
    }

    /// <summary>
    /// Get database statistics
    /// </summary>
    public void PrintStatistics()
    {
        Debug.Log($"=== Database Statistics ===");
        Debug.Log($"Total models: {database.Count}");

        var categories = database.GroupBy(e => e.category);
        foreach (var group in categories)
        {
            Debug.Log($"  {group.Key}: {group.Count()} models");
        }
    }

    // Wrapper class for JSON serialization
    [System.Serializable]
    private class DatabaseWrapper
    {
        public List<ModelEntry> entries;
    }
}
