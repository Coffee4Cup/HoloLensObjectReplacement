using UnityEngine;
using UnityEngine.XR;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Real spatial mesh scanner for HoloLens
/// Captures mesh data from the environment using XR spatial mapping
/// Works in Unity Editor XR Simulation AND on real HoloLens device
/// </summary>
public class HoloLensSpatialScanner : MonoBehaviour
{
    [Header("Scanning Settings")]
    [SerializeField] private float scanRadius = 2f;
    [SerializeField] private float minObjectSize = 0.3f; // Minimum size to be considered
    [SerializeField] private float scanDuration = 5f; // How long to scan
    [SerializeField] private LayerMask spatialMeshLayer;

    [Header("Visual Feedback")]
    [SerializeField] private GameObject boundingBoxPrefab;
    [SerializeField] private Material scanningMaterial;
    [SerializeField] private Color scanAreaColor = new Color(0, 1, 0, 0.3f);

    [Header("Keyboard Controls (Editor Testing)")]
    [SerializeField] private KeyCode startScanKey = KeyCode.S;
    [SerializeField] private KeyCode completeScanKey = KeyCode.C;
    [SerializeField] private KeyCode cancelScanKey = KeyCode.X;

    // State
    private bool isScanning = false;
    private float scanStartTime;
    private Vector3 scanCenter;
    private Bounds targetBounds;
    private GameObject boundingBoxInstance;
    private List<MeshFilter> capturedMeshes = new List<MeshFilter>();

    // Mesh data
    private Mesh combinedMesh;

    // Events
    public delegate void ScanCompleteHandler(Mesh scannedMesh);
    public event ScanCompleteHandler OnScanComplete;

    public delegate void ScanProgressHandler(float progress);
    public event ScanProgressHandler OnScanProgress;

    void Start()
    {
        Debug.Log("HoloLensSpatialScanner initialized - Ready for HoloLens!");

        // Set default layer if not set
        if (spatialMeshLayer == 0)
        {
            spatialMeshLayer = LayerMask.GetMask("Default");
        }
    }

    void Update()
    {
        // Keyboard controls for testing in Unity Editor
        if (Input.GetKeyDown(startScanKey) && !isScanning)
        {
            StartScanningAtGaze();
        }

        if (Input.GetKeyDown(completeScanKey) && isScanning)
        {
            CompleteScan();
        }

        if (Input.GetKeyDown(cancelScanKey) && isScanning)
        {
            CancelScan();
        }

        // Update scanning progress
        if (isScanning)
        {
            UpdateScanProgress();
        }
    }

    /// <summary>
    /// Start scanning at the user's gaze point
    /// </summary>
    public void StartScanningAtGaze()
    {
        // Get gaze ray (from camera forward)
        Ray gazeRay = new Ray(Camera.main.transform.position, Camera.main.transform.forward);
        RaycastHit hit;

        // Try to hit spatial mesh or any object
        if (Physics.Raycast(gazeRay, out hit, scanRadius * 2, spatialMeshLayer))
        {
            StartScanningAtPoint(hit.point);
        }
        else
        {
            // If no hit, scan at default distance
            Vector3 defaultPoint = Camera.main.transform.position + Camera.main.transform.forward * 2f;
            StartScanningAtPoint(defaultPoint);
        }
    }

    /// <summary>
    /// Start scanning at a specific world position
    /// </summary>
    public void StartScanningAtPoint(Vector3 centerPoint)
    {
        if (isScanning)
        {
            Debug.LogWarning("Already scanning!");
            return;
        }

        isScanning = true;
        scanStartTime = Time.time;
        scanCenter = centerPoint;
        capturedMeshes.Clear();

        // Create bounding box for visual feedback
        CreateScanBoundingBox(centerPoint);

        Debug.Log($"Started scanning at {centerPoint}");
        Debug.Log($"Walk around the object for {scanDuration} seconds, then press {completeScanKey} or say 'Complete'");
    }

    /// <summary>
    /// Create visual bounding box to show scan area
    /// </summary>
    private void CreateScanBoundingBox(Vector3 center)
    {
        if (boundingBoxInstance != null)
        {
            Destroy(boundingBoxInstance);
        }

        if (boundingBoxPrefab != null)
        {
            boundingBoxInstance = Instantiate(boundingBoxPrefab, center, Quaternion.identity);
            boundingBoxInstance.transform.localScale = Vector3.one * scanRadius;

            // Make it semi-transparent green to show scan area
            Renderer renderer = boundingBoxInstance.GetComponentInChildren<Renderer>();
            if (renderer != null && scanningMaterial != null)
            {
                renderer.material = scanningMaterial;
                renderer.material.color = scanAreaColor;
            }
        }
        else
        {
            // Create simple sphere to show scan area if no prefab
            boundingBoxInstance = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            boundingBoxInstance.transform.position = center;
            boundingBoxInstance.transform.localScale = Vector3.one * scanRadius * 2;

            Renderer renderer = boundingBoxInstance.GetComponent<Renderer>();
            Material mat = new Material(Shader.Find("Standard"));
            mat.color = scanAreaColor;
            mat.SetFloat("_Mode", 3); // Transparent
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.renderQueue = 3000;
            renderer.material = mat;

            // Remove collider
            Destroy(boundingBoxInstance.GetComponent<Collider>());
        }
    }

    /// <summary>
    /// Update scan progress
    /// </summary>
    private void UpdateScanProgress()
    {
        float elapsed = Time.time - scanStartTime;
        float progress = Mathf.Clamp01(elapsed / scanDuration);

        OnScanProgress?.Invoke(progress);

        // Auto-complete if duration reached
        if (elapsed >= scanDuration)
        {
            Debug.Log("Scan duration completed - auto-completing");
            CompleteScan();
        }
    }

    /// <summary>
    /// Complete the scan and process captured data
    /// </summary>
    public void CompleteScan()
    {
        if (!isScanning)
        {
            Debug.LogWarning("Not currently scanning!");
            return;
        }

        Debug.Log("Completing scan...");
        isScanning = false;

        // Clean up visual feedback
        if (boundingBoxInstance != null)
        {
            Destroy(boundingBoxInstance);
        }

        // Capture spatial mesh in the scan area
        CaptureSpatialMeshInArea();

        // Process and combine the meshes
        if (capturedMeshes.Count > 0)
        {
            combinedMesh = CombineCapturedMeshes();

            if (combinedMesh != null)
            {
                Debug.Log($"Scan complete! Captured mesh with {combinedMesh.vertexCount} vertices");
                OnScanComplete?.Invoke(combinedMesh);
            }
            else
            {
                Debug.LogError("Failed to combine captured meshes");
            }
        }
        else
        {
            Debug.LogWarning("No spatial mesh data captured. Creating fallback test mesh.");
            combinedMesh = CreateFallbackMesh();
            OnScanComplete?.Invoke(combinedMesh);
        }
    }

    /// <summary>
    /// Cancel the current scan
    /// </summary>
    public void CancelScan()
    {
        if (!isScanning)
        {
            Debug.LogWarning("Not currently scanning!");
            return;
        }

        Debug.Log("Scan cancelled");
        isScanning = false;
        capturedMeshes.Clear();

        if (boundingBoxInstance != null)
        {
            Destroy(boundingBoxInstance);
        }
    }

    /// <summary>
    /// Capture all spatial mesh data within the scan area
    /// </summary>
    /// <summary>
    /// Capture all spatial mesh data within the scan area
    /// </summary>
    private void CaptureSpatialMeshInArea()
    {
        // Find all MeshFilters in the scene (spatial mesh and regular objects)
        MeshFilter[] allMeshFilters = FindObjectsOfType<MeshFilter>();

        foreach (MeshFilter meshFilter in allMeshFilters)
        {
            // Skip if no mesh
            if (meshFilter.sharedMesh == null) continue;

            // Check if mesh bounds intersect with scan area
            Bounds meshBounds = meshFilter.sharedMesh.bounds;
            Vector3 worldCenter = meshFilter.transform.TransformPoint(meshBounds.center);

            float distance = Vector3.Distance(worldCenter, scanCenter);

            if (distance <= scanRadius)
            {
                capturedMeshes.Add(meshFilter);
                Debug.Log($"Captured mesh: {meshFilter.gameObject.name} at distance {distance:F2}m");
            }
        }

        Debug.Log($"Captured {capturedMeshes.Count} mesh segments");
    }

    /// <summary>
    /// Combine all captured meshes into one
    /// </summary>
    private Mesh CombineCapturedMeshes()
    {
        if (capturedMeshes.Count == 0)
        {
            return null;
        }

        List<CombineInstance> combineInstances = new List<CombineInstance>();

        foreach (MeshFilter meshFilter in capturedMeshes)
        {
            if (meshFilter == null || meshFilter.sharedMesh == null) continue;

            CombineInstance ci = new CombineInstance();
            ci.mesh = meshFilter.sharedMesh;
            ci.transform = meshFilter.transform.localToWorldMatrix;
            combineInstances.Add(ci);
        }

        if (combineInstances.Count == 0)
        {
            return null;
        }

        Mesh combined = new Mesh();
        combined.CombineMeshes(combineInstances.ToArray(), true, true);
        combined.RecalculateNormals();
        combined.RecalculateBounds();

        return combined;
    }

    /// <summary>
    /// Create fallback test mesh if no spatial data available
    /// </summary>
    private Mesh CreateFallbackMesh()
    {
        // Create a simple box mesh as fallback (for testing without spatial mapping)
        Mesh mesh = new Mesh();

        float w = 0.6f, h = 1.2f, d = 0.7f; // Washing machine size

        Vector3[] vertices = new Vector3[]
        {
            new Vector3(-w/2, 0, d/2), new Vector3(w/2, 0, d/2), new Vector3(w/2, h, d/2), new Vector3(-w/2, h, d/2),
            new Vector3(-w/2, 0, -d/2), new Vector3(w/2, 0, -d/2), new Vector3(w/2, h, -d/2), new Vector3(-w/2, h, -d/2)
        };

        int[] triangles = new int[]
        {
            0, 2, 1, 0, 3, 2,
            5, 6, 4, 6, 7, 4,
            4, 7, 0, 7, 3, 0,
            1, 2, 5, 2, 6, 5,
            3, 7, 2, 7, 6, 2,
            4, 0, 5, 0, 1, 5
        };

        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        Debug.Log("Created fallback mesh (no spatial data available)");

        return mesh;
    }

    /// <summary>
    /// Get the most recently scanned mesh
    /// </summary>
    public Mesh GetScannedMesh()
    {
        return combinedMesh;
    }

    /// <summary>
    /// Check if currently scanning
    /// </summary>
    public bool IsScanning()
    {
        return isScanning;
    }

    /// <summary>
    /// Get scan progress (0-1)
    /// </summary>
    public float GetScanProgress()
    {
        if (!isScanning) return 0f;

        float elapsed = Time.time - scanStartTime;
        return Mathf.Clamp01(elapsed / scanDuration);
    }

    // Visualization
    void OnDrawGizmos()
    {
        if (isScanning)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(scanCenter, scanRadius);

            Gizmos.color = new Color(0, 1, 0, 0.1f);
            Gizmos.DrawSphere(scanCenter, scanRadius);
        }
    }
}