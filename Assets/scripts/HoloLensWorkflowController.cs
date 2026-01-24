using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using TMPro;

/// <summary>
/// Production workflow controller for HoloLens
/// Uses real spatial scanning instead of test meshes
/// </summary>
public class HoloLensWorkflowController : MonoBehaviour
{
    [Header("Component References")]
    [SerializeField] private HoloLensSpatialScanner spatialScanner;
    [SerializeField] private MeshProcessor meshProcessor;
    [SerializeField] private MeshFeatureExtractor featureExtractor;
    [SerializeField] private ModelDatabase modelDatabase;
    [SerializeField] private ModelAligner modelAligner;
    
    [Header("UI")]
    [SerializeField] private TextMeshPro statusText;
    [SerializeField] private TextMeshPro instructionText;
    
    [Header("Settings")]
    [SerializeField] private int topSearchResults = 5;
    [SerializeField] private float minimumSimilarityScore = 0.3f;
    [SerializeField] private bool autoSelectBestMatch = true;

    [Header("Keyboard Controls (Editor Testing)")]
    [SerializeField] private KeyCode startWorkflowKey = KeyCode.Space;
    [SerializeField] private KeyCode resetKey = KeyCode.R;

    private enum WorkflowState
    {
        Idle,
        WaitingForScan,
        Scanning,
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
    private AlignmentResult currentAlignment;

    void Start()
    {
        // Auto-find components
        if (spatialScanner == null) spatialScanner = GetComponent<HoloLensSpatialScanner>();
        if (meshProcessor == null) meshProcessor = GetComponent<MeshProcessor>();
        if (featureExtractor == null) featureExtractor = GetComponent<MeshFeatureExtractor>();
        if (modelDatabase == null) modelDatabase = GetComponent<ModelDatabase>();
        if (modelAligner == null) modelAligner = GetComponent<ModelAligner>();
        
        // Subscribe to scanner events
        if (spatialScanner != null)
        {
            spatialScanner.OnScanComplete += OnScanComplete;
            spatialScanner.OnScanProgress += OnScanProgress;
        }
        
        UpdateStatus("Ready");
        UpdateInstructions("Press SPACE to start scanning, or say 'Scan this'");
        
        Debug.Log("HoloLensWorkflowController initialized - Production mode");
    }

    void Update()
    {
        // Keyboard controls for testing
        if (Input.GetKeyDown(startWorkflowKey) && currentState == WorkflowState.Idle)
        {
            StartWorkflow();
        }
        
        if (Input.GetKeyDown(resetKey))
        {
            ResetWorkflow();
        }
    }

    /// <summary>
    /// Start the scanning workflow
    /// </summary>
    public void StartWorkflow()
    {
        if (currentState != WorkflowState.Idle)
        {
            Debug.LogWarning("Workflow already in progress!");
            return;
        }

        currentState = WorkflowState.WaitingForScan;
        UpdateStatus("Starting scan...");
        UpdateInstructions("Look at the object you want to scan");
        
        // Start scanning at user's gaze
        StartCoroutine(DelayedStartScan());
    }

    private IEnumerator DelayedStartScan()
    {
        yield return new WaitForSeconds(0.5f);
        
        if (spatialScanner != null)
        {
            spatialScanner.StartScanningAtGaze();
            currentState = WorkflowState.Scanning;
            UpdateStatus("Scanning...");
            UpdateInstructions("Walk around the object. Press C when done.");
        }
        else
        {
            Debug.LogError("Spatial scanner not found!");
            ResetWorkflow();
        }
    }

    /// <summary>
    /// Called when spatial scanning updates progress
    /// </summary>
    private void OnScanProgress(float progress)
    {
        if (currentState == WorkflowState.Scanning)
        {
            UpdateStatus($"Scanning... {progress * 100:F0}%");
        }
    }

    /// <summary>
    /// Called when spatial scanning completes
    /// </summary>
    private void OnScanComplete(Mesh mesh)
    {
        if (currentState != WorkflowState.Scanning) return;

        scannedMesh = mesh;
        
        if (scannedMesh == null)
        {
            UpdateStatus("Error: No mesh data");
            UpdateInstructions("Try scanning again");
            currentState = WorkflowState.Idle;
            return;
        }
        
        Debug.Log($"Scan complete! Mesh has {scannedMesh.vertexCount} vertices");
        
        // Start processing pipeline
        StartCoroutine(ProcessAndMatch());
    }

    /// <summary>
    /// Process scanned mesh and search for matches
    /// </summary>
    private IEnumerator ProcessAndMatch()
    {
        currentState = WorkflowState.Processing;
        UpdateStatus("Processing mesh...");
        UpdateInstructions("Please wait...");
        yield return new WaitForSeconds(0.5f);
        
        // Process mesh
        Mesh processedMesh = meshProcessor.ProcessScannedMesh(scannedMesh);
        if (processedMesh == null)
        {
            UpdateStatus("Error: Processing failed");
            currentState = WorkflowState.Idle;
            yield break;
        }
        
        Debug.Log($"Processed mesh: {processedMesh.vertexCount} vertices");

        // Extract features
        UpdateStatus("Analyzing shape...");
        yield return new WaitForSeconds(0.3f);
        
        scannedFeatures = featureExtractor.ExtractFeatures(processedMesh);
        if (scannedFeatures == null)
        {
            UpdateStatus("Error: Feature extraction failed");
            currentState = WorkflowState.Idle;
            yield break;
        }
        
        Debug.Log($"Features extracted: {scannedFeatures}");

        // Search database
        currentState = WorkflowState.Searching;
        UpdateStatus("Searching for matches...");
        yield return new WaitForSeconds(0.3f);
        
        searchResults = modelDatabase.SearchSimilar(scannedFeatures, topSearchResults);
        searchResults = searchResults.FindAll(r => r.similarityScore >= minimumSimilarityScore);
        
        if (searchResults.Count == 0)
        {
            UpdateStatus("No matches found");
            UpdateInstructions("No similar objects in database. Press R to try again.");
            Debug.LogWarning("No models found matching criteria");
            currentState = WorkflowState.Idle;
            yield break;
        }
        
        Debug.Log($"Found {searchResults.Count} matching models");

        // Display results
        currentState = WorkflowState.DisplayingResults;
        DisplaySearchResults();
        
        yield return new WaitForSeconds(2f);

        // Auto-select best match if enabled
        if (autoSelectBestMatch)
        {
            yield return StartCoroutine(AlignAndOverlay(searchResults[0].model));
        }
        else
        {
            UpdateInstructions("Say the number to select, or say 'First one'");
        }
    }

    /// <summary>
    /// Display search results to user
    /// </summary>
    private void DisplaySearchResults()
    {
        UpdateStatus($"Found {searchResults.Count} matches!");
        
        string resultsList = "Matches:\n";
        for (int i = 0; i < Mathf.Min(3, searchResults.Count); i++)
        {
            var result = searchResults[i];
            resultsList += $"{i + 1}. {result.model.name} ({result.similarityScore * 100:F0}%)\n";
            Debug.Log($"{i + 1}. {result.model.name} - Score: {result.similarityScore:F3} - {result.matchReason}");
        }
        
        UpdateInstructions(resultsList);
    }

    /// <summary>
    /// Select and overlay a specific search result
    /// </summary>
    public void SelectResult(int index)
    {
        if (currentState != WorkflowState.DisplayingResults) return;
        if (index < 0 || index >= searchResults.Count) return;

        StartCoroutine(AlignAndOverlay(searchResults[index].model));
    }

    /// <summary>
    /// Align and overlay the selected model
    /// </summary>
    private IEnumerator AlignAndOverlay(ModelEntry modelEntry)
    {
        currentState = WorkflowState.Aligning;
        UpdateStatus($"Loading {modelEntry.name}...");
        UpdateInstructions("Please wait...");
        yield return new WaitForSeconds(0.3f);

        // Load the model
        GameObject virtualModel = modelDatabase.LoadModel(modelEntry.id);
        if (virtualModel == null)
        {
            UpdateStatus("Error: Failed to load model");
            Debug.LogError($"Could not load model: {modelEntry.name}");
            currentState = WorkflowState.Idle;
            yield break;
        }

        // Position at scan location initially
        virtualModel.transform.position = spatialScanner.transform.position + Camera.main.transform.forward * 2f;
        
        UpdateStatus("Aligning model...");
        yield return new WaitForSeconds(0.3f);

        // Align model to scanned mesh
        currentAlignment = modelAligner.AlignModel(scannedMesh, virtualModel);
        
        if (currentAlignment != null && currentAlignment.converged)
        {
            modelAligner.ApplyAlignmentToObject(virtualModel, currentAlignment);
            UpdateStatus($"Complete! (error: {currentAlignment.error:F3}m)");
            Debug.Log($"Alignment successful: {currentAlignment.iterations} iterations");
        }
        else
        {
            UpdateStatus("Model placed (alignment partial)");
            Debug.LogWarning("Alignment did not fully converge - model placed at estimated position");
        }

        overlayedModel = virtualModel;
        currentState = WorkflowState.Complete;
        
        UpdateInstructions("Model overlaid! Say 'Reset' to scan another object.");
        
        yield return new WaitForSeconds(1f);
        UpdateInstructions("Done! Press R to reset.");
    }

    /// <summary>
    /// Reset the workflow
    /// </summary>
    public void ResetWorkflow()
    {
        // Cancel scanning if in progress
        if (spatialScanner != null && spatialScanner.IsScanning())
        {
            spatialScanner.CancelScan();
        }

        // Clean up overlayed model
        if (overlayedModel != null)
        {
            Destroy(overlayedModel);
            overlayedModel = null;
        }

        // Reset state
        scannedMesh = null;
        scannedFeatures = null;
        searchResults = null;
        currentAlignment = null;

        currentState = WorkflowState.Idle;
        UpdateStatus("Ready");
        UpdateInstructions("Press SPACE to start, or say 'Scan this'");
        
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
    /// Update instruction text
    /// </summary>
    private void UpdateInstructions(string message)
    {
        if (instructionText != null)
        {
            instructionText.text = message;
        }
    }

    /// <summary>
    /// Get current workflow progress (0-1)
    /// </summary>
    public float GetProgress()
    {
        switch (currentState)
        {
            case WorkflowState.Idle: return 0f;
            case WorkflowState.WaitingForScan: return 0.1f;
            case WorkflowState.Scanning: return 0.2f + (spatialScanner != null ? spatialScanner.GetScanProgress() * 0.2f : 0);
            case WorkflowState.Processing: return 0.5f;
            case WorkflowState.Searching: return 0.7f;
            case WorkflowState.DisplayingResults: return 0.8f;
            case WorkflowState.Aligning: return 0.9f;
            case WorkflowState.Complete: return 1f;
            default: return 0f;
        }
    }

    void OnDestroy()
    {
        if (spatialScanner != null)
        {
            spatialScanner.OnScanComplete -= OnScanComplete;
            spatialScanner.OnScanProgress -= OnScanProgress;
        }
    }
}
