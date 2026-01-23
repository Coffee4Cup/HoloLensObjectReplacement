using UnityEngine;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Advanced mesh processing for cleaning and optimizing scanned meshes
/// MRTK3 Compatible - No MRTK dependencies
/// </summary>
public class MeshProcessor : MonoBehaviour
{
    [Header("Mesh Cleaning Settings")]
    [SerializeField] private float voxelSize = 0.01f; // For downsampling
    [SerializeField] private float noiseThreshold = 0.02f; // Remove small isolated components
    [SerializeField] private bool smoothMesh = true;
    [SerializeField] private int smoothingIterations = 2;

    /// <summary>
    /// Clean and optimize a raw scanned mesh
    /// </summary>
    public Mesh ProcessScannedMesh(Mesh rawMesh)
    {
        if (rawMesh == null)
        {
            Debug.LogError("Cannot process null mesh");
            return null;
        }

        Mesh processedMesh = Instantiate(rawMesh);
        
        Debug.Log($"Processing mesh: {processedMesh.vertexCount} vertices, {processedMesh.triangles.Length / 3} triangles");

        // Step 1: Remove duplicate vertices
        processedMesh = RemoveDuplicateVertices(processedMesh);
        
        // Step 2: Remove isolated vertices and small components
        processedMesh = RemoveNoiseComponents(processedMesh);
        
        // Step 3: Smooth the mesh
        if (smoothMesh)
        {
            processedMesh = LaplacianSmooth(processedMesh, smoothingIterations);
        }
        
        // Step 4: Downsample if needed (voxel grid filter)
        processedMesh = VoxelDownsample(processedMesh, voxelSize);
        
        // Step 5: Recalculate normals and bounds
        processedMesh.RecalculateNormals();
        processedMesh.RecalculateBounds();
        
        Debug.Log($"Processed mesh: {processedMesh.vertexCount} vertices, {processedMesh.triangles.Length / 3} triangles");
        
        return processedMesh;
    }

    /// <summary>
    /// Remove duplicate vertices within a tolerance
    /// </summary>
    private Mesh RemoveDuplicateVertices(Mesh mesh)
    {
        Vector3[] vertices = mesh.vertices;
        int[] triangles = mesh.triangles;
        Vector3[] normals = mesh.normals;
        
        Dictionary<Vector3, int> uniqueVertices = new Dictionary<Vector3, int>();
        List<Vector3> newVertices = new List<Vector3>();
        List<Vector3> newNormals = new List<Vector3>();
        int[] vertexRemap = new int[vertices.Length];
        
        float tolerance = 0.0001f;
        
        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 v = vertices[i];
            Vector3 roundedV = new Vector3(
                Mathf.Round(v.x / tolerance) * tolerance,
                Mathf.Round(v.y / tolerance) * tolerance,
                Mathf.Round(v.z / tolerance) * tolerance
            );
            
            if (!uniqueVertices.ContainsKey(roundedV))
            {
                uniqueVertices[roundedV] = newVertices.Count;
                newVertices.Add(v);
                if (normals.Length > 0)
                {
                    newNormals.Add(normals[i]);
                }
            }
            
            vertexRemap[i] = uniqueVertices[roundedV];
        }
        
        // Remap triangles
        int[] newTriangles = new int[triangles.Length];
        for (int i = 0; i < triangles.Length; i++)
        {
            newTriangles[i] = vertexRemap[triangles[i]];
        }
        
        Mesh newMesh = new Mesh();
        newMesh.vertices = newVertices.ToArray();
        newMesh.triangles = newTriangles;
        if (newNormals.Count > 0)
        {
            newMesh.normals = newNormals.ToArray();
        }
        
        return newMesh;
    }

    /// <summary>
    /// Remove small disconnected components (noise)
    /// </summary>
    private Mesh RemoveNoiseComponents(Mesh mesh)
    {
        Vector3[] vertices = mesh.vertices;
        int[] triangles = mesh.triangles;
        
        // Build adjacency list
        Dictionary<int, HashSet<int>> adjacency = new Dictionary<int, HashSet<int>>();
        for (int i = 0; i < vertices.Length; i++)
        {
            adjacency[i] = new HashSet<int>();
        }
        
        for (int i = 0; i < triangles.Length; i += 3)
        {
            int v0 = triangles[i];
            int v1 = triangles[i + 1];
            int v2 = triangles[i + 2];
            
            adjacency[v0].Add(v1);
            adjacency[v0].Add(v2);
            adjacency[v1].Add(v0);
            adjacency[v1].Add(v2);
            adjacency[v2].Add(v0);
            adjacency[v2].Add(v1);
        }
        
        // Find connected components using BFS
        List<HashSet<int>> components = new List<HashSet<int>>();
        HashSet<int> visited = new HashSet<int>();
        
        for (int i = 0; i < vertices.Length; i++)
        {
            if (!visited.Contains(i))
            {
                HashSet<int> component = new HashSet<int>();
                Queue<int> queue = new Queue<int>();
                queue.Enqueue(i);
                visited.Add(i);
                
                while (queue.Count > 0)
                {
                    int current = queue.Dequeue();
                    component.Add(current);
                    
                    foreach (int neighbor in adjacency[current])
                    {
                        if (!visited.Contains(neighbor))
                        {
                            visited.Add(neighbor);
                            queue.Enqueue(neighbor);
                        }
                    }
                }
                
                components.Add(component);
            }
        }
        
        // Find the largest component
        HashSet<int> largestComponent = components.OrderByDescending(c => c.Count).First();
        
        // Create new mesh with only the largest component
        Dictionary<int, int> oldToNewIndex = new Dictionary<int, int>();
        List<Vector3> newVertices = new List<Vector3>();
        
        int newIndex = 0;
        foreach (int oldIndex in largestComponent)
        {
            oldToNewIndex[oldIndex] = newIndex++;
            newVertices.Add(vertices[oldIndex]);
        }
        
        List<int> newTriangles = new List<int>();
        for (int i = 0; i < triangles.Length; i += 3)
        {
            int v0 = triangles[i];
            int v1 = triangles[i + 1];
            int v2 = triangles[i + 2];
            
            if (largestComponent.Contains(v0) && largestComponent.Contains(v1) && largestComponent.Contains(v2))
            {
                newTriangles.Add(oldToNewIndex[v0]);
                newTriangles.Add(oldToNewIndex[v1]);
                newTriangles.Add(oldToNewIndex[v2]);
            }
        }
        
        Mesh newMesh = new Mesh();
        newMesh.vertices = newVertices.ToArray();
        newMesh.triangles = newTriangles.ToArray();
        
        return newMesh;
    }

    /// <summary>
    /// Laplacian smoothing to reduce noise
    /// </summary>
    private Mesh LaplacianSmooth(Mesh mesh, int iterations)
    {
        Vector3[] vertices = mesh.vertices;
        int[] triangles = mesh.triangles;
        
        // Build adjacency list
        Dictionary<int, List<int>> adjacency = new Dictionary<int, List<int>>();
        for (int i = 0; i < vertices.Length; i++)
        {
            adjacency[i] = new List<int>();
        }
        
        for (int i = 0; i < triangles.Length; i += 3)
        {
            int v0 = triangles[i];
            int v1 = triangles[i + 1];
            int v2 = triangles[i + 2];
            
            if (!adjacency[v0].Contains(v1)) adjacency[v0].Add(v1);
            if (!adjacency[v0].Contains(v2)) adjacency[v0].Add(v2);
            if (!adjacency[v1].Contains(v0)) adjacency[v1].Add(v0);
            if (!adjacency[v1].Contains(v2)) adjacency[v1].Add(v2);
            if (!adjacency[v2].Contains(v0)) adjacency[v2].Add(v0);
            if (!adjacency[v2].Contains(v1)) adjacency[v2].Add(v1);
        }
        
        // Perform iterations of smoothing
        for (int iter = 0; iter < iterations; iter++)
        {
            Vector3[] newVertices = new Vector3[vertices.Length];
            
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 average = Vector3.zero;
                List<int> neighbors = adjacency[i];
                
                if (neighbors.Count > 0)
                {
                    foreach (int neighbor in neighbors)
                    {
                        average += vertices[neighbor];
                    }
                    average /= neighbors.Count;
                    
                    // Blend between original and average (gentle smoothing)
                    newVertices[i] = Vector3.Lerp(vertices[i], average, 0.5f);
                }
                else
                {
                    newVertices[i] = vertices[i];
                }
            }
            
            vertices = newVertices;
        }
        
        Mesh smoothedMesh = new Mesh();
        smoothedMesh.vertices = vertices;
        smoothedMesh.triangles = triangles;
        
        return smoothedMesh;
    }

    /// <summary>
    /// Voxel-based downsampling to reduce mesh density
    /// </summary>
    private Mesh VoxelDownsample(Mesh mesh, float voxelSize)
    {
        Vector3[] vertices = mesh.vertices;
        int[] triangles = mesh.triangles;
        
        // Create voxel grid
        Dictionary<Vector3Int, List<int>> voxelGrid = new Dictionary<Vector3Int, List<int>>();
        
        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3Int voxelCoord = new Vector3Int(
                Mathf.FloorToInt(vertices[i].x / voxelSize),
                Mathf.FloorToInt(vertices[i].y / voxelSize),
                Mathf.FloorToInt(vertices[i].z / voxelSize)
            );
            
            if (!voxelGrid.ContainsKey(voxelCoord))
            {
                voxelGrid[voxelCoord] = new List<int>();
            }
            voxelGrid[voxelCoord].Add(i);
        }
        
        // Average vertices in each voxel
        Dictionary<int, int> oldToNewIndex = new Dictionary<int, int>();
        List<Vector3> newVertices = new List<Vector3>();
        
        foreach (var kvp in voxelGrid)
        {
            Vector3 average = Vector3.zero;
            foreach (int vertexIndex in kvp.Value)
            {
                average += vertices[vertexIndex];
            }
            average /= kvp.Value.Count;
            
            int newIndex = newVertices.Count;
            newVertices.Add(average);
            
            foreach (int vertexIndex in kvp.Value)
            {
                oldToNewIndex[vertexIndex] = newIndex;
            }
        }
        
        // Remap triangles and remove degenerate ones
        List<int> newTriangles = new List<int>();
        for (int i = 0; i < triangles.Length; i += 3)
        {
            int v0 = oldToNewIndex[triangles[i]];
            int v1 = oldToNewIndex[triangles[i + 1]];
            int v2 = oldToNewIndex[triangles[i + 2]];
            
            // Skip degenerate triangles
            if (v0 != v1 && v1 != v2 && v2 != v0)
            {
                newTriangles.Add(v0);
                newTriangles.Add(v1);
                newTriangles.Add(v2);
            }
        }
        
        Mesh downsampledMesh = new Mesh();
        downsampledMesh.vertices = newVertices.ToArray();
        downsampledMesh.triangles = newTriangles.ToArray();
        
        return downsampledMesh;
    }

    /// <summary>
    /// Calculate mesh volume (for feature extraction)
    /// </summary>
    public float CalculateMeshVolume(Mesh mesh)
    {
        Vector3[] vertices = mesh.vertices;
        int[] triangles = mesh.triangles;
        
        float volume = 0f;
        
        for (int i = 0; i < triangles.Length; i += 3)
        {
            Vector3 v0 = vertices[triangles[i]];
            Vector3 v1 = vertices[triangles[i + 1]];
            Vector3 v2 = vertices[triangles[i + 2]];
            
            // Signed volume of tetrahedron formed by origin and triangle
            volume += Vector3.Dot(v0, Vector3.Cross(v1, v2)) / 6f;
        }
        
        return Mathf.Abs(volume);
    }

    /// <summary>
    /// Calculate mesh surface area
    /// </summary>
    public float CalculateSurfaceArea(Mesh mesh)
    {
        Vector3[] vertices = mesh.vertices;
        int[] triangles = mesh.triangles;
        
        float area = 0f;
        
        for (int i = 0; i < triangles.Length; i += 3)
        {
            Vector3 v0 = vertices[triangles[i]];
            Vector3 v1 = vertices[triangles[i + 1]];
            Vector3 v2 = vertices[triangles[i + 2]];
            
            // Area of triangle using cross product
            Vector3 edge1 = v1 - v0;
            Vector3 edge2 = v2 - v0;
            area += Vector3.Cross(edge1, edge2).magnitude * 0.5f;
        }
        
        return area;
    }
}