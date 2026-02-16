using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using TMPro;

/// <summary>
/// Simplified workflow controller that works with MRTK3
/// Uses test meshes instead of spatial scanning initially
/// FIXED: Lowered threshold, added diagnostics
/// </summary>
public class SimpleWorkflowController : MonoBehaviour
{
    [Header("Component References")]
    [SerializeField] private SimpleMeshTest meshTest;
    [SerializeField] private MeshProcessor meshProcessor;
    [SerializeField] private MeshFeatureExtractor featureExtractor;
    [SerializeField] private ModelDatabase modelDatabase;
    [SerializeField] private ModelAligner modelAligner;
    
    [Header("UI")]
    [SerializeField] private TextMeshPro statusText;

    [Header("Settings")]
    [SerializeField] private int topSearchResults = 5;
    [SerializeField] private float minimumSimilarityScore = 0.15f; // FIXED: was 0.3f

    private enum WorkflowState
    {
        Idle,
        Processing,
        Searching,
        DisplayingResults,
        Aligning,
        Complete
    }

    private WorkflowState currentState = WorkflowState.Idle;
    private Mesh scannedMesh;
    private MeshFeatures scannedFeatures;
    private List<SearchResult> searchResults;
    private GameObject overlayedModel;

    void Start()
    {
        // Auto-find components if not assigned
        if (meshTest == null) meshTest = GetComponent<SimpleMeshTest>();
        if (meshProcessor == null) meshProcessor = GetComponent<MeshProcessor>();
        if (featureExtractor == null) featureExtractor = GetComponent<MeshFeatureExtractor>();
        if (modelDatabase == null) modelDatabase = GetComponent<ModelDatabase>();
        if (modelAligner == null) modelAligner = GetComponent<ModelAligner>();
        
        UpdateStatus("Ready - Press SPACE to start test workflow");
        Debug.Log("SimpleWorkflowController initialized (MRTK3 compatible)");
    }

    void Update()
    {
        // Keyboard controls for testing
        if (Input.GetKeyDown(KeyCode.Space) && currentState == WorkflowState.Idle)
        {
            StartWorkflow();
        }
        
        if (Input.GetKeyDown(KeyCode.R))
        {
            ResetWorkflow();
        }
    }

    /// <summary>
    /// Start the complete workflow with test mesh
    /// </summary>
    public void StartWorkflow()
    {
        if (currentState != WorkflowState.Idle)
        {
            Debug.LogWarning("Workflow already in progress!");
            return;
        }

        UpdateStatus("Starting workflow...");
        StartCoroutine(RunWorkflow());
    }

    /// <summary>
    /// Main workflow coroutine
    /// FIXED: Added diagnostic logging, reports scores even on failure
    /// </summary>
    private IEnumerator RunWorkflow()
    {
        // Step 1: Get test mesh
        UpdateStatus("Getting test mesh...");
        yield return new WaitForSeconds(0.5f);
        
        scannedMesh = meshTest.GetTestMesh();
        if (scannedMesh == null)
        {
            UpdateStatus("ERROR: No test mesh available. Press T first.");
            currentState = WorkflowState.Idle;
            yield break;
        }
        
        Debug.Log($"Got test mesh with {scannedMesh.vertexCount} vertices");

        // Step 2: Process mesh
        currentState = WorkflowState.Processing;
        UpdateStatus("Processing mesh...");
        yield return new WaitForSeconds(0.5f);
        
        Mesh processedMesh = meshProcessor.ProcessScannedMesh(scannedMesh);
        if (processedMesh == null)
        {
            UpdateStatus("ERROR: Mesh processing failed");
            currentState = WorkflowState.Idle;
            yield break;
        }
        
        Debug.Log($"Processed mesh: {processedMesh.vertexCount} vertices");

        // Step 3: Extract features
        UpdateStatus("Extracting features...");
        yield return new WaitForSeconds(0.5f);
        
        scannedFeatures = featureExtractor.ExtractFeatures(processedMesh);
        if (scannedFeatures == null)
        {
            UpdateStatus("ERROR: Feature extraction failed");
            currentState = WorkflowState.Idle;
            yield break;
        }
        
        Debug.Log($"Features extracted: {scannedFeatures}");
        Debug.Log($"[DIAG] Scanned BBox: {scannedFeatures.boundingBoxSize}, " +
                  $"Volume: {scannedFeatures.volume:F4}, " +
                  $"Compactness: {scannedFeatures.compactness:F3}, " +
                  $"Category: {scannedFeatures.suggestedCategory}");

        // Step 4: Search database
        currentState = WorkflowState.Searching;
        UpdateStatus("Searching database...");
        yield return new WaitForSeconds(0.5f);
        
        // Get ALL results first (unfiltered) for diagnostics
        List<SearchResult> allResults = modelDatabase.SearchSimilar(scannedFeatures, topSearchResults);

        Debug.Log($"[DIAG] {allResults.Count} results before threshold filter:");
        for (int i = 0; i < allResults.Count; i++)
        {
            var r = allResults[i];
            Debug.Log($"[DIAG]   {i + 1}. {r.model.name} — score: {r.similarityScore:F3} — {r.matchReason}");
        }

        // Apply threshold
        searchResults = allResults.FindAll(r => r.similarityScore >= minimumSimilarityScore);
        
        if (searchResults.Count == 0)
        {
            float bestScore = allResults.Count > 0 ? allResults[0].similarityScore : 0f;
            string bestName = allResults.Count > 0 ? allResults[0].model.name : "N/A";

            UpdateStatus($"No matches (best: {bestName} @ {bestScore:F3}, need >= {minimumSimilarityScore:F2})");
            Debug.LogWarning($"No models above threshold {minimumSimilarityScore}. " +
                             $"Best was '{bestName}' at {bestScore:F3}. Database might be empty.");
            currentState = WorkflowState.Idle;
            yield break;
        }
        
        Debug.Log($"Found {searchResults.Count} matching models");

        // Step 5: Display results
        currentState = WorkflowState.DisplayingResults;
        UpdateStatus($"Found {searchResults.Count} matches!");
        
        // Show top 3 results in console
        for (int i = 0; i < Mathf.Min(3, searchResults.Count); i++)
        {
            var result = searchResults[i];
            Debug.Log($"{i + 1}. {result.model.name} - Score: {result.similarityScore:F3} - {result.matchReason}");
        }
        
        yield return new WaitForSeconds(2f);

        // Step 6: Auto-select best match
        UpdateStatus("Loading best match...");
        yield return StartCoroutine(AlignAndOverlay(searchResults[0].model));
    }

    /// <summary>
    /// Align and overlay the selected model
    /// </summary>
    private IEnumerator AlignAndOverlay(ModelEntry modelEntry)
    {
        currentState = WorkflowState.Aligning;
        UpdateStatus($"Aligning {modelEntry.name}...");
        yield return new WaitForSeconds(0.5f);

        // Load the model
        GameObject virtualModel = modelDatabase.LoadModel(modelEntry.id);
        if (virtualModel == null)
        {
            UpdateStatus("ERROR: Failed to load model");
            Debug.LogError($"Could not load model: {modelEntry.name}");
            currentState = WorkflowState.Idle;
            yield break;
        }

        // Position it in front of user
        virtualModel.transform.position = new Vector3(0, 0.6f, 2);
        
        // Try to align
        AlignmentResult alignment = modelAligner.AlignModel(scannedMesh, virtualModel);
        
        if (alignment != null)
        {
            modelAligner.ApplyAlignmentToObject(virtualModel, alignment);
            UpdateStatus($"Complete! Model aligned (error: {alignment.error:F4}m)");
            Debug.Log($"Alignment successful: {alignment.iterations} iterations, error: {alignment.error:F4}");
        }
        else
        {
            UpdateStatus($"Model loaded (alignment skipped)");
            Debug.LogWarning("Alignment failed, model placed at default position");
        }

        // Scale the model AFTER alignment (alignment overwrites localScale)
        Renderer modelRenderer = virtualModel.GetComponentInChildren<Renderer>();
        if (modelRenderer != null)
        {
            Vector3 currentWorldSize = modelRenderer.bounds.size;
            Vector3 targetSize = modelEntry.features != null ? modelEntry.features.boundingBoxSize : scannedFeatures.boundingBoxSize;

            if (targetSize.x < 0.01f || targetSize.y < 0.01f || targetSize.z < 0.01f)
            {
                targetSize = scannedFeatures.boundingBoxSize;
                Debug.LogWarning("[Scale] DB entry has invalid size, using scanned size as fallback");
            }

            float scaleX = (currentWorldSize.x > 0.001f) ? targetSize.x / currentWorldSize.x : 1f;
            float scaleY = (currentWorldSize.y > 0.001f) ? targetSize.y / currentWorldSize.y : 1f;
            float scaleZ = (currentWorldSize.z > 0.001f) ? targetSize.z / currentWorldSize.z : 1f;

            float uniformScale = (scaleX + scaleY + scaleZ) / 3f;
            virtualModel.transform.localScale *= uniformScale;

            Debug.Log($"[Scale] World size after align: {currentWorldSize}, Target size: {targetSize}, " +
                      $"Applied uniform scale: {uniformScale:F3}");
        }

        overlayedModel = virtualModel;
        currentState = WorkflowState.Complete;
        
        yield return new WaitForSeconds(1f);
        UpdateStatus("Done! Press R to reset, SPACE to try again");
    }

    /// <summary>
    /// Reset the workflow
    /// </summary>
    public void ResetWorkflow()
    {
        if (overlayedModel != null)
        {
            Destroy(overlayedModel);
            overlayedModel = null;
        }

        scannedMesh = null;
        scannedFeatures = null;
        searchResults = null;

        currentState = WorkflowState.Idle;
        UpdateStatus("Reset - Press SPACE to start, T to create test mesh");
        
        Debug.Log("Workflow reset");
    }

    /// <summary>
    /// Update status text
    /// </summary>
    private void UpdateStatus(string message)
    {
        if (statusText != null)
        {
            statusText.text = message;
        }
        Debug.Log($"Status: {message}");
    }

    /// <summary>
    /// Get current progress
    /// </summary>
    public float GetProgress()
    {
        switch (currentState)
        {
            case WorkflowState.Idle: return 0f;
            case WorkflowState.Processing: return 0.33f;
            case WorkflowState.Searching: return 0.66f;
            case WorkflowState.DisplayingResults: return 0.8f;
            case WorkflowState.Aligning: return 0.9f;
            case WorkflowState.Complete: return 1f;
            default: return 0f;
        }
    }
}
