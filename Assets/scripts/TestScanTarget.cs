using UnityEngine;

/// <summary>
/// Creates a scannable test mesh that simulates what HoloLens spatial mapping
/// would produce when scanning a washing machine.
/// 
/// USAGE:
///   1. Create an empty GameObject in the scene
///   2. Add this script to it
///   3. Position it where you want the "real" washing machine to be
///   4. Press Play — it creates a noisy box mesh the scanner can capture
///   5. Press S to scan, scanner picks up this mesh (not the DB models)
///   6. The matching pipeline identifies it and overlays the best DB model
///
/// Press G at runtime to regenerate with different noise.
/// </summary>
public class TestScanTarget : MonoBehaviour
{
    [Header("Simulated Object Size (meters)")]
    [SerializeField] private float width = 0.60f;   // ~washing machine width
    [SerializeField] private float height = 0.85f;   // ~washing machine height
    [SerializeField] private float depth = 0.60f;    // ~washing machine depth

    [Header("Spatial Mesh Simulation")]
    [SerializeField] private float noiseAmount = 0.02f;  // Simulates scanner noise
    [SerializeField] private int subdivisions = 4;        // More = denser mesh
    [SerializeField] private Color meshColor = new Color(0.3f, 0.8f, 0.3f, 0.5f);

    [Header("Options")]
    [SerializeField] private bool generateOnStart = true;
    [SerializeField] private KeyCode regenerateKey = KeyCode.G;

    private MeshFilter meshFilter;
    private MeshRenderer meshRenderer;

    void Start()
    {
        if (generateOnStart)
        {
            GenerateScanTarget();
        }
    }

    void Update()
    {
        if (Input.GetKeyDown(regenerateKey))
        {
            GenerateScanTarget();
            Debug.Log("[TestScanTarget] Regenerated scan target mesh");
        }
    }

    /// <summary>
    /// Generate a subdivided, noisy box mesh that looks like spatial mesh data
    /// </summary>
    public void GenerateScanTarget()
    {
        // Setup components
        meshFilter = GetComponent<MeshFilter>();
        if (meshFilter == null) meshFilter = gameObject.AddComponent<MeshFilter>();

        meshRenderer = GetComponent<MeshRenderer>();
        if (meshRenderer == null) meshRenderer = gameObject.AddComponent<MeshRenderer>();

        // Create material
        Material mat = new Material(Shader.Find("Standard"));
        mat.color = meshColor;
        mat.SetFloat("_Mode", 3); // Transparent
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZWrite", 0);
        mat.DisableKeyword("_ALPHATEST_ON");
        mat.EnableKeyword("_ALPHABLEND_ON");
        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        mat.renderQueue = 3000;
        meshRenderer.material = mat;

        // Generate the mesh
        Mesh mesh = CreateSubdividedBox(width, height, depth, subdivisions, noiseAmount);
        meshFilter.mesh = mesh;

        // Add collider so raycast can hit it
        MeshCollider col = GetComponent<MeshCollider>();
        if (col == null) col = gameObject.AddComponent<MeshCollider>();
        col.sharedMesh = mesh;

        Debug.Log($"[TestScanTarget] Created scan target: {width:F2}×{height:F2}×{depth:F2}m, " +
                  $"{mesh.vertexCount} vertices, {mesh.triangles.Length / 3} triangles");
    }

    /// <summary>
    /// Create a subdivided box mesh with noise to simulate spatial scanning
    /// </summary>
    private Mesh CreateSubdividedBox(float w, float h, float d, int subdiv, float noise)
    {
        Mesh mesh = new Mesh();

        // Generate vertices and triangles for each face
        var allVerts = new System.Collections.Generic.List<Vector3>();
        var allTris = new System.Collections.Generic.List<int>();

        // 6 faces of the box
        // Front face (Z+)
        AddSubdividedQuad(allVerts, allTris,
            new Vector3(-w/2, 0, d/2), new Vector3(w/2, 0, d/2),
            new Vector3(w/2, h, d/2), new Vector3(-w/2, h, d/2),
            subdiv, noise);

        // Back face (Z-)
        AddSubdividedQuad(allVerts, allTris,
            new Vector3(w/2, 0, -d/2), new Vector3(-w/2, 0, -d/2),
            new Vector3(-w/2, h, -d/2), new Vector3(w/2, h, -d/2),
            subdiv, noise);

        // Left face (X-)
        AddSubdividedQuad(allVerts, allTris,
            new Vector3(-w/2, 0, -d/2), new Vector3(-w/2, 0, d/2),
            new Vector3(-w/2, h, d/2), new Vector3(-w/2, h, -d/2),
            subdiv, noise);

        // Right face (X+)
        AddSubdividedQuad(allVerts, allTris,
            new Vector3(w/2, 0, d/2), new Vector3(w/2, 0, -d/2),
            new Vector3(w/2, h, -d/2), new Vector3(w/2, h, d/2),
            subdiv, noise);

        // Top face (Y+)
        AddSubdividedQuad(allVerts, allTris,
            new Vector3(-w/2, h, d/2), new Vector3(w/2, h, d/2),
            new Vector3(w/2, h, -d/2), new Vector3(-w/2, h, -d/2),
            subdiv, noise);

        // Bottom face (Y=0)
        AddSubdividedQuad(allVerts, allTris,
            new Vector3(-w/2, 0, -d/2), new Vector3(w/2, 0, -d/2),
            new Vector3(w/2, 0, d/2), new Vector3(-w/2, 0, d/2),
            subdiv, noise);

        mesh.vertices = allVerts.ToArray();
        mesh.triangles = allTris.ToArray();
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        return mesh;
    }

    /// <summary>
    /// Add a subdivided quad (face) with noise
    /// </summary>
    private void AddSubdividedQuad(
        System.Collections.Generic.List<Vector3> verts,
        System.Collections.Generic.List<int> tris,
        Vector3 bl, Vector3 br, Vector3 tr, Vector3 tl,
        int subdivisions, float noise)
    {
        int baseIndex = verts.Count;
        int res = subdivisions + 1;

        // Generate vertices in a grid
        for (int y = 0; y <= subdivisions; y++)
        {
            for (int x = 0; x <= subdivisions; x++)
            {
                float u = (float)x / subdivisions;
                float v = (float)y / subdivisions;

                // Bilinear interpolation
                Vector3 bottom = Vector3.Lerp(bl, br, u);
                Vector3 top = Vector3.Lerp(tl, tr, u);
                Vector3 pos = Vector3.Lerp(bottom, top, v);

                // Add noise (simulates spatial mesh inaccuracy)
                // Don't add noise to edge vertices to keep the box shape recognizable
                if (x > 0 && x < subdivisions && y > 0 && y < subdivisions)
                {
                    pos += new Vector3(
                        Random.Range(-noise, noise),
                        Random.Range(-noise, noise),
                        Random.Range(-noise, noise));
                }

                verts.Add(pos);
            }
        }

        // Generate triangles
        for (int y = 0; y < subdivisions; y++)
        {
            for (int x = 0; x < subdivisions; x++)
            {
                int i0 = baseIndex + y * res + x;
                int i1 = baseIndex + y * res + (x + 1);
                int i2 = baseIndex + (y + 1) * res + (x + 1);
                int i3 = baseIndex + (y + 1) * res + x;

                tris.Add(i0); tris.Add(i2); tris.Add(i1);
                tris.Add(i0); tris.Add(i3); tris.Add(i2);
            }
        }
    }

    /// <summary>
    /// Get the generated mesh for external use
    /// </summary>
    public Mesh GetMesh()
    {
        return meshFilter != null ? meshFilter.mesh : null;
    }

    void OnDrawGizmos()
    {
        // Show the target area in Scene view even before Play
        Gizmos.color = new Color(0, 1, 0, 0.3f);
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawWireCube(new Vector3(0, height / 2f, 0), new Vector3(width, height, depth));
    }
}
