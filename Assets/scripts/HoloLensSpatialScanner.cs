using UnityEngine;
using UnityEngine.XR;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Real spatial mesh scanner for HoloLens
/// Auto-excludes objects tagged "DatabaseModel" and name-matched washing machines
/// </summary>
public class HoloLensSpatialScanner : MonoBehaviour
{
    [Header("Scanning Settings")]
    [SerializeField] private float scanRadius = 2f;
    [SerializeField] private float minObjectSize = 0.3f;
    [SerializeField] private float scanDuration = 5f;
    [SerializeField] private LayerMask spatialMeshLayer;

    [Header("Exclusion Settings")]
    [SerializeField] private string excludeTag = "DatabaseModel";
    [SerializeField] private List<GameObject> excludeFromScan = new List<GameObject>();

    [Header("Visual Feedback")]
    [SerializeField] private GameObject boundingBoxPrefab;
    [SerializeField] private Material scanningMaterial;
    [SerializeField] private Color scanAreaColor = new Color(0, 1, 0, 0.3f);

    [Header("Keyboard Controls (Editor Testing)")]
    [SerializeField] private KeyCode startScanKey = KeyCode.S;
    [SerializeField] private KeyCode completeScanKey = KeyCode.C;
    [SerializeField] private KeyCode cancelScanKey = KeyCode.X;

    private bool isScanning = false;
    private float scanStartTime;
    private Vector3 scanCenter;
    private Bounds targetBounds;
    private GameObject boundingBoxInstance;
    private List<MeshFilter> capturedMeshes = new List<MeshFilter>();
    private Mesh combinedMesh;
    private bool tagExists = false;

    public delegate void ScanCompleteHandler(Mesh scannedMesh);
    public event ScanCompleteHandler OnScanComplete;
    public delegate void ScanProgressHandler(float progress);
    public event ScanProgressHandler OnScanProgress;

    void Start()
    {
        Debug.Log("HoloLensSpatialScanner initialized - Ready for HoloLens!");
        if (spatialMeshLayer == 0) spatialMeshLayer = LayerMask.GetMask("Default");

        try { GameObject.FindGameObjectsWithTag(excludeTag); tagExists = true; }
        catch (UnityException) { tagExists = false; }
        
        // Auto-find scene washing machines and add to exclude list
        AutoExcludeSceneModels();
    }

    /// <summary>
    /// Automatically find and exclude all washing machine objects in the scene
    /// </summary>
    private void AutoExcludeSceneModels()
    {
        // Find by name pattern
        string[] patterns = { "washingmachine", "washing_machine", "washer" };
        foreach (GameObject go in FindObjectsOfType<GameObject>())
        {
            string nameLower = go.name.ToLower();
            foreach (string pattern in patterns)
            {
                if (nameLower.Contains(pattern))
                {
                    // Add the root of this object (not children individually)
                    Transform root = go.transform;
                    while (root.parent != null && root.parent.name.ToLower().Contains(pattern))
                        root = root.parent;
                    
                    if (!excludeFromScan.Contains(root.gameObject))
                    {
                        excludeFromScan.Add(root.gameObject);
                        Debug.Log($"[Scanner] Auto-excluding '{root.name}' (DB model)");
                    }
                    break;
                }
            }
        }
        
        Debug.Log($"[Scanner] Total excluded objects: {excludeFromScan.Count}");
    }

    void Update()
    {
        if (Input.GetKeyDown(startScanKey) && !isScanning) StartScanningAtGaze();
        if (Input.GetKeyDown(completeScanKey) && isScanning) CompleteScan();
        if (Input.GetKeyDown(cancelScanKey) && isScanning) CancelScan();
        if (isScanning) UpdateScanProgress();
    }

    public void ExcludeObject(GameObject obj)
    {
        if (obj != null && !excludeFromScan.Contains(obj))
        {
            excludeFromScan.Add(obj);
            Debug.Log($"[Scanner] Excluding '{obj.name}' from scan");
        }
    }

    public void StartScanningAtGaze()
    {
        Ray gazeRay = new Ray(Camera.main.transform.position, Camera.main.transform.forward);
        RaycastHit hit;
        if (Physics.Raycast(gazeRay, out hit, scanRadius * 2, spatialMeshLayer))
            StartScanningAtPoint(hit.point);
        else
            StartScanningAtPoint(Camera.main.transform.position + Camera.main.transform.forward * 2f);
    }

    public void StartScanningAtPoint(Vector3 centerPoint)
    {
        if (isScanning) { Debug.LogWarning("Already scanning!"); return; }
        isScanning = true;
        scanStartTime = Time.time;
        scanCenter = centerPoint;
        capturedMeshes.Clear();
        CreateScanBoundingBox(centerPoint);
        Debug.Log($"Started scanning at {centerPoint}");
    }

    private void CreateScanBoundingBox(Vector3 center)
    {
        if (boundingBoxInstance != null) Destroy(boundingBoxInstance);

        if (boundingBoxPrefab != null)
        {
            boundingBoxInstance = Instantiate(boundingBoxPrefab, center, Quaternion.identity);
            boundingBoxInstance.transform.localScale = Vector3.one * scanRadius;
            Renderer renderer = boundingBoxInstance.GetComponentInChildren<Renderer>();
            if (renderer != null && scanningMaterial != null)
            {
                renderer.material = scanningMaterial;
                renderer.material.color = scanAreaColor;
            }
        }
        else
        {
            boundingBoxInstance = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            boundingBoxInstance.transform.position = center;
            boundingBoxInstance.transform.localScale = Vector3.one * scanRadius * 2;
            Renderer renderer = boundingBoxInstance.GetComponent<Renderer>();
            Material mat = new Material(Shader.Find("Standard"));
            mat.color = scanAreaColor;
            mat.SetFloat("_Mode", 3);
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.renderQueue = 3000;
            renderer.material = mat;
            Destroy(boundingBoxInstance.GetComponent<Collider>());
        }
    }

    private void UpdateScanProgress()
    {
        float elapsed = Time.time - scanStartTime;
        float progress = Mathf.Clamp01(elapsed / scanDuration);
        OnScanProgress?.Invoke(progress);
        if (elapsed >= scanDuration)
        {
            Debug.Log("Scan duration completed - auto-completing");
            CompleteScan();
        }
    }

    public void CompleteScan()
    {
        if (!isScanning) { Debug.LogWarning("Not currently scanning!"); return; }
        Debug.Log("Completing scan...");
        isScanning = false;
        if (boundingBoxInstance != null) Destroy(boundingBoxInstance);

        CaptureSpatialMeshInArea();

        if (capturedMeshes.Count > 0)
        {
            combinedMesh = CombineCapturedMeshes();
            if (combinedMesh != null)
            {
                Debug.Log($"Scan complete! Captured mesh with {combinedMesh.vertexCount} vertices");
                OnScanComplete?.Invoke(combinedMesh);
            }
            else Debug.LogError("Failed to combine captured meshes");
        }
        else
        {
            Debug.LogWarning("No spatial mesh data captured in range. Using fallback.");
            combinedMesh = CreateFallbackMesh();
            OnScanComplete?.Invoke(combinedMesh);
        }
    }

    public void CancelScan()
    {
        if (!isScanning) { Debug.LogWarning("Not currently scanning!"); return; }
        Debug.Log("Scan cancelled");
        isScanning = false;
        capturedMeshes.Clear();
        if (boundingBoxInstance != null) Destroy(boundingBoxInstance);
    }

    private bool IsExcluded(GameObject go)
    {
        // Check tag on self and parents
        if (tagExists)
        {
            Transform t = go.transform;
            while (t != null)
            {
                try { if (t.CompareTag(excludeTag)) return true; } catch { }
                t = t.parent;
            }
        }

        // Check manual exclude list (includes auto-excluded)
        foreach (GameObject excludeObj in excludeFromScan)
        {
            if (excludeObj != null &&
                (go == excludeObj || go.transform.IsChildOf(excludeObj.transform)))
                return true;
        }

        return false;
    }

    private void CaptureSpatialMeshInArea()
    {
        MeshFilter[] allMeshFilters = FindObjectsOfType<MeshFilter>();
        int filteredDB = 0, filteredUI = 0, filteredNonReadable = 0, filteredTooSmall = 0;

        foreach (MeshFilter meshFilter in allMeshFilters)
        {
            if (meshFilter.sharedMesh == null) continue;

            if (boundingBoxInstance != null &&
                meshFilter.transform.IsChildOf(boundingBoxInstance.transform))
            { filteredUI++; continue; }

            if (IsExcluded(meshFilter.gameObject))
            { filteredDB++; continue; }

            if (meshFilter.GetComponentInParent<Canvas>() != null ||
                meshFilter.GetComponentInParent<TMPro.TMP_Text>() != null)
            { filteredUI++; continue; }

            if (!meshFilter.sharedMesh.isReadable)
            { filteredNonReadable++; continue; }

            Bounds meshBounds;
            Renderer rend = meshFilter.GetComponent<Renderer>();
            if (rend != null) meshBounds = rend.bounds;
            else meshBounds = new Bounds(
                meshFilter.transform.TransformPoint(meshFilter.sharedMesh.bounds.center),
                meshFilter.sharedMesh.bounds.size);

            float distance = Vector3.Distance(meshBounds.center, scanCenter);
            if (distance > scanRadius) continue;

            float maxExtent = Mathf.Max(meshBounds.extents.x, meshBounds.extents.y, meshBounds.extents.z);
            if (maxExtent < 0.01f) { filteredTooSmall++; continue; }

            capturedMeshes.Add(meshFilter);
            Debug.Log($"[Scan] Captured: {meshFilter.gameObject.name} " +
                      $"({meshFilter.sharedMesh.vertexCount} verts, dist={distance:F2}m, " +
                      $"worldSize={meshBounds.size})");
        }

        Debug.Log($"[Scan] Result: {capturedMeshes.Count} captured | " +
                  $"Excluded: {filteredDB} DB models, {filteredUI} UI, " +
                  $"{filteredNonReadable} non-readable, {filteredTooSmall} too small");
    }

    private Mesh CombineCapturedMeshes()
    {
        if (capturedMeshes.Count == 0) return null;
        List<CombineInstance> combineInstances = new List<CombineInstance>();
        foreach (MeshFilter mf in capturedMeshes)
        {
            if (mf == null || mf.sharedMesh == null) continue;
            CombineInstance ci = new CombineInstance();
            ci.mesh = mf.sharedMesh;
            ci.transform = mf.transform.localToWorldMatrix;
            combineInstances.Add(ci);
        }
        if (combineInstances.Count == 0) return null;
        Mesh combined = new Mesh();
        combined.CombineMeshes(combineInstances.ToArray(), true, true);
        combined.RecalculateNormals();
        combined.RecalculateBounds();
        return combined;
    }

    private Mesh CreateFallbackMesh()
    {
        Mesh mesh = new Mesh();
        float w = 0.6f, h = 1.2f, d = 0.7f;
        Vector3[] verts = new Vector3[] {
            new Vector3(-w/2,0,d/2), new Vector3(w/2,0,d/2), new Vector3(w/2,h,d/2), new Vector3(-w/2,h,d/2),
            new Vector3(-w/2,0,-d/2), new Vector3(w/2,0,-d/2), new Vector3(w/2,h,-d/2), new Vector3(-w/2,h,-d/2)
        };
        int[] tris = { 0,2,1, 0,3,2, 5,6,4, 6,7,4, 4,7,0, 7,3,0, 1,2,5, 2,6,5, 3,7,2, 7,6,2, 4,0,5, 0,1,5 };
        mesh.vertices = verts;
        mesh.triangles = tris;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        Debug.Log("Created fallback mesh (washing machine size 0.6×1.2×0.7)");
        return mesh;
    }

    public Mesh GetScannedMesh() { return combinedMesh; }
    public bool IsScanning() { return isScanning; }
    public float GetScanProgress()
    {
        if (!isScanning) return 0f;
        return Mathf.Clamp01((Time.time - scanStartTime) / scanDuration);
    }

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
