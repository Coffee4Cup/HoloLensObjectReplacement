using UnityEngine;
using UnityEngine.Windows.Speech;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Voice command handler for HoloLens
/// Works in Unity Editor (with microphone) AND on HoloLens device
/// </summary>
public class VoiceCommandHandler : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private HoloLensWorkflowController workflowController;
    [SerializeField] private HoloLensSpatialScanner spatialScanner;
    
    [Header("Settings")]
    [SerializeField] private bool enableVoiceCommands = true;
    [SerializeField] private float confidenceThreshold = 0.5f;

    // Voice recognition
    private KeywordRecognizer keywordRecognizer;
    private Dictionary<string, System.Action> keywords = new Dictionary<string, System.Action>();

    void Start()
    {
        // Auto-find components
        if (workflowController == null)
            workflowController = GetComponent<HoloLensWorkflowController>();
        
        if (spatialScanner == null)
            spatialScanner = GetComponent<HoloLensSpatialScanner>();

        if (enableVoiceCommands)
        {
            SetupVoiceCommands();
        }
    }

    /// <summary>
    /// Setup voice command recognition
    /// </summary>
    private void SetupVoiceCommands()
    {
        // Define voice commands and their actions
        keywords.Add("scan this", () => {
            Debug.Log("Voice: Scan this");
            if (workflowController != null)
                workflowController.StartWorkflow();
        });

        keywords.Add("start scan", () => {
            Debug.Log("Voice: Start scan");
            if (workflowController != null)
                workflowController.StartWorkflow();
        });

        keywords.Add("complete", () => {
            Debug.Log("Voice: Complete");
            if (spatialScanner != null && spatialScanner.IsScanning())
                spatialScanner.CompleteScan();
        });

        keywords.Add("finish scan", () => {
            Debug.Log("Voice: Finish scan");
            if (spatialScanner != null && spatialScanner.IsScanning())
                spatialScanner.CompleteScan();
        });

        keywords.Add("done scanning", () => {
            Debug.Log("Voice: Done scanning");
            if (spatialScanner != null && spatialScanner.IsScanning())
                spatialScanner.CompleteScan();
        });

        keywords.Add("cancel", () => {
            Debug.Log("Voice: Cancel");
            if (spatialScanner != null && spatialScanner.IsScanning())
                spatialScanner.CancelScan();
            else if (workflowController != null)
                workflowController.ResetWorkflow();
        });

        keywords.Add("reset", () => {
            Debug.Log("Voice: Reset");
            if (workflowController != null)
                workflowController.ResetWorkflow();
        });

        keywords.Add("start over", () => {
            Debug.Log("Voice: Start over");
            if (workflowController != null)
                workflowController.ResetWorkflow();
        });

        keywords.Add("first one", () => {
            Debug.Log("Voice: First one");
            if (workflowController != null)
                workflowController.SelectResult(0);
        });

        keywords.Add("second one", () => {
            Debug.Log("Voice: Second one");
            if (workflowController != null)
                workflowController.SelectResult(1);
        });

        keywords.Add("third one", () => {
            Debug.Log("Voice: Third one");
            if (workflowController != null)
                workflowController.SelectResult(2);
        });

        // Create keyword recognizer
        keywordRecognizer = new KeywordRecognizer(keywords.Keys.ToArray());
        keywordRecognizer.OnPhraseRecognized += OnPhraseRecognized;
        keywordRecognizer.Start();

        Debug.Log($"Voice commands enabled! Listening for {keywords.Count} commands:");
        foreach (var keyword in keywords.Keys)
        {
            Debug.Log($"  - '{keyword}'");
        }
    }

    /// <summary>
    /// Called when a voice command is recognized
    /// </summary>
    private void OnPhraseRecognized(PhraseRecognizedEventArgs args)
    {
        // Check confidence level
        if (args.confidence < (ConfidenceLevel)confidenceThreshold)
        {
            Debug.LogWarning($"Voice command '{args.text}' recognized but confidence too low: {args.confidence}");
            return;
        }

        Debug.Log($"Voice command recognized: '{args.text}' (confidence: {args.confidence})");

        // Execute the associated action
        if (keywords.ContainsKey(args.text))
        {
            keywords[args.text].Invoke();
        }
    }

    /// <summary>
    /// Enable voice commands
    /// </summary>
    public void EnableVoice()
    {
        if (keywordRecognizer != null && !keywordRecognizer.IsRunning)
        {
            keywordRecognizer.Start();
            Debug.Log("Voice commands enabled");
        }
    }

    /// <summary>
    /// Disable voice commands
    /// </summary>
    public void DisableVoice()
    {
        if (keywordRecognizer != null && keywordRecognizer.IsRunning)
        {
            keywordRecognizer.Stop();
            Debug.Log("Voice commands disabled");
        }
    }

    void OnDestroy()
    {
        if (keywordRecognizer != null)
        {
            keywordRecognizer.OnPhraseRecognized -= OnPhraseRecognized;
            keywordRecognizer.Stop();
            keywordRecognizer.Dispose();
        }
    }

    void OnApplicationQuit()
    {
        if (keywordRecognizer != null)
        {
            keywordRecognizer.Stop();
            keywordRecognizer.Dispose();
        }
    }
}
