using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using TMPro;

/// <summary>
/// Production workflow controller for HoloLens
/// FIXED: Scale after alignment, diagnostic logging, proper threshold
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
    [SerializeField] private float minimumSimilarityScore = 0.15f;
    [SerializeField] private bool autoSelectBestMatch = true;

    [Header("Keyboard Controls (Editor Testing)")]
    [SerializeField] private KeyCode startWorkflowKey = KeyCode.Space;
    [SerializeField] private KeyCode resetKey = KeyCode.R;

    private enum WorkflowState
    {
        Idle, WaitingForScan, Scanning, Processing,
        Searching, DisplayingResults, Aligning, Complete
    }

    private WorkflowState currentState = WorkflowState.Idle;
    private Mesh scannedMesh;
    private MeshFeatures scannedFeatures;
    private List<SearchResult> searchResults;
    private GameObject overlayedModel;
    private AlignmentResult currentAlignment;

    void Start()
    {
        if (spatialScanner == null) spatialScanner = GetComponent<HoloLensSpatialScanner>();
        if (meshProcessor == null) meshProcessor = GetComponent<MeshProcessor>();
        if (featureExtractor == null) featureExtractor = GetComponent<MeshFeatureExtractor>();
        if (modelDatabase == null) modelDatabase = GetComponent<ModelDatabase>();
        if (modelAligner == null) modelAligner = GetComponent<ModelAligner>();
        
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
        if (Input.GetKeyDown(startWorkflowKey) && currentState == WorkflowState.Idle)
            StartWorkflow();
        if (Input.GetKeyDown(resetKey))
            ResetWorkflow();
    }

    public void StartWorkflow()
    {
        if (currentState != WorkflowState.Idle)
        { Debug.LogWarning("Workflow already in progress!"); return; }

        currentState = WorkflowState.WaitingForScan;
        UpdateStatus("Starting scan...");
        UpdateInstructions("Look at the object you want to scan");
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

    private void OnScanProgress(float progress)
    {
        if (currentState == WorkflowState.Scanning)
            UpdateStatus($"Scanning... {progress * 100:F0}%");
    }

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
        StartCoroutine(ProcessAndMatch());
    }

    private IEnumerator ProcessAndMatch()
    {
        currentState = WorkflowState.Processing;
        UpdateStatus("Processing mesh...");
        UpdateInstructions("Please wait...");
        yield return new WaitForSeconds(0.5f);
        
        Mesh processedMesh = meshProcessor.ProcessScannedMesh(scannedMesh);
        if (processedMesh == null)
        {
            UpdateStatus("Error: Processing failed");
            currentState = WorkflowState.Idle;
            yield break;
        }
        Debug.Log($"Processed mesh: {processedMesh.vertexCount} vertices");

        UpdateStatus("Analyzing shape...");
        yield return new WaitForSeconds(0.3f);
        
        scannedFeatures = featureExtractor.ExtractFeatures(processedMesh);
        if (scannedFeatures == null)
        {
            UpdateStatus("Error: Feature extraction failed");
            currentState = WorkflowState.Idle;
            yield break;
        }
        
        Debug.Log($"[DIAG] Scanned: BBox={scannedFeatures.boundingBoxSize}, " +
                  $"Vol={scannedFeatures.volume:F4}, Compact={scannedFeatures.compactness:F3}, " +
                  $"Cat={scannedFeatures.suggestedCategory}");

        Vector3 bbox = scannedFeatures.boundingBoxSize;
        float minDim = Mathf.Min(bbox.x, bbox.y, bbox.z);
        float maxDim = Mathf.Max(bbox.x, bbox.y, bbox.z);
        if (minDim < 0.05f || maxDim < 0.1f)
            Debug.LogWarning($"[DIAG] Scan looks very thin/small! " +
                             $"Min={minDim:F3}m Max={maxDim:F3}m — may have missed the real object");

        currentState = WorkflowState.Searching;
        UpdateStatus("Searching for matches...");
        yield return new WaitForSeconds(0.3f);
        
        List<SearchResult> allResults = modelDatabase.SearchSimilar(scannedFeatures, topSearchResults);

        Debug.Log($"[DIAG] {allResults.Count} results before threshold:");
        for (int i = 0; i < allResults.Count; i++)
        {
            var r = allResults[i];
            Debug.Log($"[DIAG]   {i+1}. {r.model.name} — score={r.similarityScore:F3} — {r.matchReason}");
            if (r.model.features != null)
                Debug.Log($"[DIAG]      DB BBox={r.model.features.boundingBoxSize}, " +
                          $"Vol={r.model.features.volume:F4}");
        }

        searchResults = allResults.FindAll(r => r.similarityScore >= minimumSimilarityScore);
        
        if (searchResults.Count == 0)
        {
            float bestScore = allResults.Count > 0 ? allResults[0].similarityScore : 0f;
            string bestName = allResults.Count > 0 ? allResults[0].model.name : "N/A";
            UpdateStatus($"No matches (best: {bestName} @ {bestScore:F3})");
            UpdateInstructions("Try scanning closer. Press R to retry.");
            currentState = WorkflowState.Idle;
            yield break;
        }
        
        currentState = WorkflowState.DisplayingResults;
        DisplaySearchResults();
        yield return new WaitForSeconds(2f);

        if (autoSelectBestMatch)
            yield return StartCoroutine(AlignAndOverlay(searchResults[0].model));
        else
            UpdateInstructions("Say the number to select, or say 'First one'");
    }

    private void DisplaySearchResults()
    {
        UpdateStatus($"Found {searchResults.Count} matches!");
        string resultsList = "Matches:\n";
        for (int i = 0; i < Mathf.Min(3, searchResults.Count); i++)
        {
            var r = searchResults[i];
            resultsList += $"{i+1}. {r.model.name} ({r.similarityScore*100:F0}%)\n";
            Debug.Log($"{i+1}. {r.model.name} - Score: {r.similarityScore:F3} - {r.matchReason}");
        }
        UpdateInstructions(resultsList);
    }

    public void SelectResult(int index)
    {
        if (currentState != WorkflowState.DisplayingResults) return;
        if (index < 0 || index >= searchResults.Count) return;
        StartCoroutine(AlignAndOverlay(searchResults[index].model));
    }

    private IEnumerator AlignAndOverlay(ModelEntry modelEntry)
    {
        currentState = WorkflowState.Aligning;
        UpdateStatus($"Loading {modelEntry.name}...");
        UpdateInstructions("Please wait...");
        yield return new WaitForSeconds(0.3f);

        GameObject virtualModel = modelDatabase.LoadModel(modelEntry.id);
        if (virtualModel == null)
        {
            UpdateStatus("Error: Failed to load model");
            currentState = WorkflowState.Idle;
            yield break;
        }

        // Exclude the clone from future scans
        if (spatialScanner != null) spatialScanner.ExcludeObject(virtualModel);

        // Position at scan location
        virtualModel.transform.position = spatialScanner.transform.position + Camera.main.transform.forward * 2f;
        
        UpdateStatus("Aligning model...");
        yield return new WaitForSeconds(0.3f);

        // Run alignment
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
            Debug.LogWarning("Alignment did not fully converge");
        }

        // SCALE AFTER ALIGNMENT (alignment overwrites localScale)
        // Use the DB entry's real-world bounding box as the target size
        Renderer modelRenderer = virtualModel.GetComponentInChildren<Renderer>();
        if (modelRenderer != null)
        {
            Vector3 currentWorldSize = modelRenderer.bounds.size;
            Vector3 targetSize = Vector3.zero;

            // Prefer DB features (real-world size from AddModel)
            if (modelEntry.features != null &&
                modelEntry.features.boundingBoxSize.x > 0.05f &&
                modelEntry.features.boundingBoxSize.y > 0.05f)
            {
                targetSize = modelEntry.features.boundingBoxSize;
            }
            else
            {
                // Fallback to scanned size
                targetSize = scannedFeatures.boundingBoxSize;
                Debug.LogWarning("[Scale] DB entry has tiny bbox, using scanned size as fallback");
            }

            if (currentWorldSize.x > 0.001f && currentWorldSize.y > 0.001f && currentWorldSize.z > 0.001f)
            {
                float scaleX = targetSize.x / currentWorldSize.x;
                float scaleY = targetSize.y / currentWorldSize.y;
                float scaleZ = targetSize.z / currentWorldSize.z;
                float uniformScale = (scaleX + scaleY + scaleZ) / 3f;
                virtualModel.transform.localScale *= uniformScale;

                Debug.Log($"[Scale] CurrentSize={currentWorldSize}, Target={targetSize}, " +
                          $"UniformScale={uniformScale:F3}");
            }
        }

        overlayedModel = virtualModel;
        currentState = WorkflowState.Complete;
        UpdateInstructions("Model overlaid! Press R to reset.");
        yield return new WaitForSeconds(1f);
        UpdateInstructions("Done! Press R to reset.");
    }

    public void ResetWorkflow()
    {
        if (spatialScanner != null && spatialScanner.IsScanning())
            spatialScanner.CancelScan();

        if (overlayedModel != null)
        {
            Destroy(overlayedModel);
            overlayedModel = null;
        }

        scannedMesh = null;
        scannedFeatures = null;
        searchResults = null;
        currentAlignment = null;
        currentState = WorkflowState.Idle;
        UpdateStatus("Ready");
        UpdateInstructions("Press SPACE to start, or say 'Scan this'");
        Debug.Log("Workflow reset");
    }

    private void UpdateStatus(string message)
    {
        if (statusText != null) statusText.text = message;
        Debug.Log($"Status: {message}");
    }

    private void UpdateInstructions(string message)
    {
        if (instructionText != null) instructionText.text = message;
    }

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
