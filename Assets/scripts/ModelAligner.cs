using UnityEngine;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Alignment result containing transform and quality metrics
/// MRTK3 Compatible
/// </summary>
public class AlignmentResult
{
    public Vector3 position;
    public Quaternion rotation;
    public Vector3 scale;
    public float error; // Average distance after alignment
    public int iterations; // Number of ICP iterations performed
    public bool converged;
    
    public Matrix4x4 TransformMatrix
    {
        get
        {
            return Matrix4x4.TRS(position, rotation, scale);
        }
    }
}

/// <summary>
/// Aligns a virtual 3D model to a scanned real-world mesh using ICP algorithm
/// </summary>
public class ModelAligner : MonoBehaviour
{
    [Header("ICP Settings")]
    [SerializeField] private int maxIterations = 50;
    [SerializeField] private float convergenceThreshold = 0.001f;
    [SerializeField] private float maxCorrespondenceDistance = 0.5f;
    [SerializeField] private int samplingRate = 10; // Use every Nth vertex for performance
    
    [Header("Visualization")]
    [SerializeField] private bool showDebugGizmos = true;
    [SerializeField] private Color sourceMeshColor = Color.blue;
    [SerializeField] private Color targetMeshColor = Color.red;

    private Vector3[] sourcePoints; // Scanned mesh points
    private Vector3[] targetPoints; // Virtual model points
    private Vector3[] transformedPoints; // Current transformation of target

    /// <summary>
    /// Align a virtual model to match a scanned mesh
    /// </summary>
    /// <param name="scannedMesh">The real-world scanned mesh</param>
    /// <param name="virtualModel">The virtual model to align</param>
    /// <returns>Alignment result with transform and error metrics</returns>
    public AlignmentResult AlignModel(Mesh scannedMesh, GameObject virtualModel)
    {
        if (scannedMesh == null || virtualModel == null)
        {
            Debug.LogError("Cannot align: null mesh or model");
            return null;
        }

        // Extract point clouds
        sourcePoints = SamplePointCloud(scannedMesh.vertices, samplingRate);
        
        MeshFilter modelMesh = virtualModel.GetComponentInChildren<MeshFilter>();
        if (modelMesh == null)
        {
            Debug.LogError("Virtual model has no mesh!");
            return null;
        }
        
        targetPoints = SamplePointCloud(modelMesh.sharedMesh.vertices, samplingRate);
        transformedPoints = new Vector3[targetPoints.Length];

        Debug.Log($"Aligning {targetPoints.Length} target points to {sourcePoints.Length} source points");

        // Initial alignment using bounding box
        AlignmentResult result = InitialAlignment(scannedMesh.bounds, modelMesh.sharedMesh.bounds);
        
        // Apply initial transform
        ApplyTransform(result.TransformMatrix);

        // Refine with ICP
        result = IterativeClosestPoint(result);

        Debug.Log($"Alignment complete: {result.iterations} iterations, error: {result.error:F4}");

        return result;
    }

    /// <summary>
    /// Initial coarse alignment based on bounding boxes and centroids
    /// </summary>
    private AlignmentResult InitialAlignment(Bounds sourceBounds, Bounds targetBounds)
    {
        AlignmentResult result = new AlignmentResult();

        // Align centroids
        result.position = sourceBounds.center - targetBounds.center;

        // Scale to match bounding box size
        Vector3 sourceSize = sourceBounds.size;
        Vector3 targetSize = targetBounds.size;
        
        float scaleX = sourceSize.x / targetSize.x;
        float scaleY = sourceSize.y / targetSize.y;
        float scaleZ = sourceSize.z / targetSize.z;
        
        // Use uniform scale (average) for stability
        float uniformScale = (scaleX + scaleY + scaleZ) / 3f;
        result.scale = Vector3.one * uniformScale;

        // Start with identity rotation
        result.rotation = Quaternion.identity;

        result.error = float.MaxValue;
        result.iterations = 0;
        result.converged = false;

        return result;
    }

    /// <summary>
    /// Iterative Closest Point algorithm
    /// </summary>
    private AlignmentResult IterativeClosestPoint(AlignmentResult initialGuess)
    {
        AlignmentResult result = initialGuess;
        float previousError = float.MaxValue;

        for (int iter = 0; iter < maxIterations; iter++)
        {
            // Step 1: Find closest point correspondences
            List<(int source, int target)> correspondences = FindCorrespondences();

            if (correspondences.Count < 3)
            {
                Debug.LogWarning("Too few correspondences found");
                break;
            }

            // Step 2: Compute optimal transformation
            (Vector3 translation, Quaternion rotation, Vector3 scale) = ComputeTransform(correspondences);

            // Step 3: Apply transformation
            result.position += translation;
            result.rotation = rotation * result.rotation;
            // Keep scale fixed after initial alignment
            
            Matrix4x4 transform = Matrix4x4.TRS(translation, rotation, Vector3.one);
            ApplyTransform(transform);

            // Step 4: Compute alignment error
            float error = ComputeAlignmentError(correspondences);
            result.error = error;
            result.iterations = iter + 1;

            // Check convergence
            if (Mathf.Abs(previousError - error) < convergenceThreshold)
            {
                result.converged = true;
                Debug.Log($"Converged at iteration {iter + 1}");
                break;
            }

            previousError = error;

            // Debug output every 10 iterations
            if (iter % 10 == 0)
            {
                Debug.Log($"Iteration {iter}: error = {error:F4}, correspondences = {correspondences.Count}");
            }
        }

        return result;
    }

    /// <summary>
    /// Find closest point correspondences between source and transformed target
    /// </summary>
    private List<(int source, int target)> FindCorrespondences()
    {
        List<(int source, int target)> correspondences = new List<(int, int)>();

        for (int i = 0; i < transformedPoints.Length; i++)
        {
            float minDist = float.MaxValue;
            int closestIdx = -1;

            // Find closest source point
            for (int j = 0; j < sourcePoints.Length; j++)
            {
                float dist = Vector3.Distance(transformedPoints[i], sourcePoints[j]);
                if (dist < minDist && dist < maxCorrespondenceDistance)
                {
                    minDist = dist;
                    closestIdx = j;
                }
            }

            if (closestIdx >= 0)
            {
                correspondences.Add((closestIdx, i));
            }
        }

        return correspondences;
    }

    /// <summary>
    /// Compute optimal transformation based on correspondences
    /// Uses SVD-based point set registration
    /// </summary>
    private (Vector3 translation, Quaternion rotation, Vector3 scale) ComputeTransform(List<(int source, int target)> correspondences)
    {
        // Compute centroids
        Vector3 sourceCentroid = Vector3.zero;
        Vector3 targetCentroid = Vector3.zero;

        foreach (var (s, t) in correspondences)
        {
            sourceCentroid += sourcePoints[s];
            targetCentroid += transformedPoints[t];
        }

        sourceCentroid /= correspondences.Count;
        targetCentroid /= correspondences.Count;

        // Center the point sets
        Vector3[] sourceCentered = new Vector3[correspondences.Count];
        Vector3[] targetCentered = new Vector3[correspondences.Count];

        for (int i = 0; i < correspondences.Count; i++)
        {
            sourceCentered[i] = sourcePoints[correspondences[i].source] - sourceCentroid;
            targetCentered[i] = transformedPoints[correspondences[i].target] - targetCentroid;
        }

        // Compute cross-covariance matrix H
        Matrix4x4 H = Matrix4x4.zero;
        for (int i = 0; i < correspondences.Count; i++)
        {
            Vector3 s = sourceCentered[i];
            Vector3 t = targetCentered[i];

            H[0, 0] += t.x * s.x;
            H[0, 1] += t.x * s.y;
            H[0, 2] += t.x * s.z;
            H[1, 0] += t.y * s.x;
            H[1, 1] += t.y * s.y;
            H[1, 2] += t.y * s.z;
            H[2, 0] += t.z * s.x;
            H[2, 1] += t.z * s.y;
            H[2, 2] += t.z * s.z;
        }

        // Simplified rotation extraction using quaternion
        // For production, use proper SVD
        Quaternion rotation = ExtractRotation(H);

        // Compute translation
        Vector3 translation = sourceCentroid - rotation * targetCentroid;

        return (translation, rotation, Vector3.one);
    }

    /// <summary>
    /// Extract rotation from cross-covariance matrix
    /// Simplified version - for production use proper SVD
    /// </summary>
    private Quaternion ExtractRotation(Matrix4x4 H)
    {
        // This is a simplified approximation
        // For robust ICP, implement proper SVD-based rotation extraction
        
        // Extract rotation axis and angle from the matrix
        Vector3 axis = new Vector3(
            H[2, 1] - H[1, 2],
            H[0, 2] - H[2, 0],
            H[1, 0] - H[0, 1]
        );

        float magnitude = axis.magnitude;
        if (magnitude < 0.001f)
        {
            return Quaternion.identity;
        }

        axis /= magnitude;
        float angle = Mathf.Atan2(magnitude, H[0, 0] + H[1, 1] + H[2, 2] - 1);

        return Quaternion.AngleAxis(angle * Mathf.Rad2Deg, axis);
    }

    /// <summary>
    /// Apply transformation to target points
    /// </summary>
    private void ApplyTransform(Matrix4x4 transform)
    {
        for (int i = 0; i < targetPoints.Length; i++)
        {
            transformedPoints[i] = transform.MultiplyPoint3x4(transformedPoints[i]);
        }
    }

    /// <summary>
    /// Compute average alignment error
    /// </summary>
    private float ComputeAlignmentError(List<(int source, int target)> correspondences)
    {
        float totalError = 0f;

        foreach (var (s, t) in correspondences)
        {
            totalError += Vector3.Distance(sourcePoints[s], transformedPoints[t]);
        }

        return totalError / correspondences.Count;
    }

    /// <summary>
    /// Sample point cloud for performance
    /// </summary>
    private Vector3[] SamplePointCloud(Vector3[] points, int stride)
    {
        List<Vector3> sampled = new List<Vector3>();
        for (int i = 0; i < points.Length; i += stride)
        {
            sampled.Add(points[i]);
        }
        return sampled.ToArray();
    }

    /// <summary>
    /// Apply alignment result to a GameObject
    /// </summary>
    public void ApplyAlignmentToObject(GameObject obj, AlignmentResult alignment)
    {
        if (obj == null || alignment == null) return;

        obj.transform.position = alignment.position;
        obj.transform.rotation = alignment.rotation;
        obj.transform.localScale = alignment.scale;
    }

    /// <summary>
    /// Fine-tune alignment with user adjustment
    /// </summary>
    public AlignmentResult RefineAlignment(AlignmentResult current, Vector3 positionOffset, Quaternion rotationOffset)
    {
        AlignmentResult refined = new AlignmentResult
        {
            position = current.position + positionOffset,
            rotation = rotationOffset * current.rotation,
            scale = current.scale,
            error = current.error,
            iterations = current.iterations,
            converged = current.converged
        };

        return refined;
    }

    // Visualization
    void OnDrawGizmos()
    {
        if (!showDebugGizmos) return;

        if (sourcePoints != null)
        {
            Gizmos.color = sourceMeshColor;
            foreach (Vector3 p in sourcePoints)
            {
                Gizmos.DrawSphere(p, 0.01f);
            }
        }

        if (transformedPoints != null)
        {
            Gizmos.color = targetMeshColor;
            foreach (Vector3 p in transformedPoints)
            {
                Gizmos.DrawSphere(p, 0.01f);
            }
        }
    }
}