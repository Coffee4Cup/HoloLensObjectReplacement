using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Simple mesh scanning test that doesn't depend on MRTK spatial awareness
/// Use this to test the core functionality first
/// </summary>
public class SimpleMeshTest : MonoBehaviour
{
    [Header("Test Settings")]
    [SerializeField] private bool createTestMesh = true;
    [SerializeField] private GameObject boundingBoxPrefab;
    
    private Mesh testMesh;
    private GameObject instantiatedBoundingBox;

    void Start()
    {
        Debug.Log("SimpleMeshTest started - MRTK3 compatible");
        
        if (createTestMesh)
        {
            CreateTestWashingMachineMesh();
        }
    }

    void Update()
    {
        // Press T to create test mesh
        if (Input.GetKeyDown(KeyCode.T))
        {
            CreateTestWashingMachineMesh();
            Debug.Log("Test mesh created with T key");
        }
        
        // Press B to show bounding box
        if (Input.GetKeyDown(KeyCode.B))
        {
            ShowBoundingBox();
            Debug.Log("Bounding box shown with B key");
        }
    }

    /// <summary>
    /// Create a simple box-shaped mesh to simulate a washing machine
    /// </summary>
    void CreateTestWashingMachineMesh()
    {
        testMesh = CreateBoxMesh(0.6f, 1.2f, 0.7f);
        
        // Create a GameObject to display it
        GameObject testObject = new GameObject("TestScannedMesh");
        testObject.transform.position = new Vector3(0, 0.6f, 2);
        
        MeshFilter mf = testObject.AddComponent<MeshFilter>();
        mf.mesh = testMesh;
        
        MeshRenderer mr = testObject.AddComponent<MeshRenderer>();
        mr.material = new Material(Shader.Find("Standard"));
        mr.material.color = new Color(0.5f, 0.5f, 1f, 0.5f);
        
        Debug.Log($"Created test mesh with {testMesh.vertexCount} vertices");
    }

    /// <summary>
    /// Create a box mesh programmatically
    /// </summary>
    Mesh CreateBoxMesh(float width, float height, float depth)
    {
        Mesh mesh = new Mesh();
        
        Vector3[] vertices = new Vector3[]
        {
            // Front face
            new Vector3(-width/2, 0, depth/2),
            new Vector3(width/2, 0, depth/2),
            new Vector3(width/2, height, depth/2),
            new Vector3(-width/2, height, depth/2),
            // Back face
            new Vector3(-width/2, 0, -depth/2),
            new Vector3(width/2, 0, -depth/2),
            new Vector3(width/2, height, -depth/2),
            new Vector3(-width/2, height, -depth/2),
        };
        
        int[] triangles = new int[]
        {
            // Front
            0, 2, 1, 0, 3, 2,
            // Back
            5, 6, 4, 6, 7, 4,
            // Left
            4, 7, 0, 7, 3, 0,
            // Right
            1, 2, 5, 2, 6, 5,
            // Top
            3, 7, 2, 7, 6, 2,
            // Bottom
            4, 0, 5, 0, 1, 5
        };
        
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        
        return mesh;
    }

    /// <summary>
    /// Show bounding box around test mesh
    /// </summary>
    void ShowBoundingBox()
    {
        if (boundingBoxPrefab != null)
        {
            if (instantiatedBoundingBox != null)
            {
                Destroy(instantiatedBoundingBox);
            }
            
            instantiatedBoundingBox = Instantiate(boundingBoxPrefab);
            instantiatedBoundingBox.transform.position = new Vector3(0, 0.6f, 2);
            instantiatedBoundingBox.transform.localScale = new Vector3(0.6f, 1.2f, 0.7f);
            
            Debug.Log("Bounding box created");
        }
        else
        {
            Debug.LogWarning("Bounding box prefab not assigned!");
        }
    }

    /// <summary>
    /// Get the test mesh for other components to use
    /// </summary>
    public Mesh GetTestMesh()
    {
        return testMesh;
    }
}
