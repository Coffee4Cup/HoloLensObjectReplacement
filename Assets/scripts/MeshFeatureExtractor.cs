using UnityEngine;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Feature descriptor for a 3D mesh
/// MRTK3 Compatible
/// </summary>
[System.Serializable]
public class MeshFeatures
{
    // Basic geometric features
    public Vector3 boundingBoxSize;
    public Vector3 centroid;
    public float volume;
    public float surfaceArea;
    
    // Aspect ratios
    public float aspectRatioXY; // width/height
    public float aspectRatioXZ; // width/depth
    public float aspectRatioYZ; // height/depth
    
    // Shape compactness
    public float compactness; // How sphere-like the shape is
    public float sphericity;
    
    // Orientation
    public Vector3[] principalAxes; // From PCA
    public float[] eigenvalues;
    
    // Distribution features
    public float[] volumeDistribution; // Volume in octants
    public float[] surfaceDistribution; // Surface area in octants
    
    // Shape histogram (simplified shape descriptor)
    public float[] shapeHistogram; // 32 bins
    
    // Category hints (for faster search)
    public string suggestedCategory; // "appliance", "furniture", etc.
    
    public override string ToString()
    {
        return $"BBox: {boundingBoxSize}, Volume: {volume:F3}, Area: {surfaceArea:F3}, Compactness: {compactness:F3}";
    }
}

/// <summary>
/// Extracts geometric features from meshes for similarity matching
/// </summary>
public class MeshFeatureExtractor : MonoBehaviour
{
    private MeshProcessor meshProcessor;
    
    void Awake()
    {
        meshProcessor = GetComponent<MeshProcessor>();
        if (meshProcessor == null)
        {
            meshProcessor = gameObject.AddComponent<MeshProcessor>();
        }
    }

    /// <summary>
    /// Extract all features from a mesh
    /// </summary>
    public MeshFeatures ExtractFeatures(Mesh mesh)
    {
        if (mesh == null)
        {
            Debug.LogError("Cannot extract features from null mesh");
            return null;
        }

        MeshFeatures features = new MeshFeatures();
        
        Vector3[] vertices = mesh.vertices;
        
        // Basic features
        features.boundingBoxSize = mesh.bounds.size;
        features.centroid = CalculateCentroid(vertices);
        features.volume = meshProcessor.CalculateMeshVolume(mesh);
        features.surfaceArea = meshProcessor.CalculateSurfaceArea(mesh);
        
        // Aspect ratios (normalized to largest dimension)
        Vector3 size = features.boundingBoxSize;
        float maxDim = Mathf.Max(size.x, size.y, size.z);
        features.aspectRatioXY = size.x / size.y;
        features.aspectRatioXZ = size.x / size.z;
        features.aspectRatioYZ = size.y / size.z;
        
        // Compactness measures
        features.compactness = CalculateCompactness(features.surfaceArea, features.volume);
        features.sphericity = CalculateSphericity(features.surfaceArea, features.volume);
        
        // Principal Component Analysis for orientation
        (features.principalAxes, features.eigenvalues) = ComputePCA(vertices);
        
        // Spatial distribution features
        features.volumeDistribution = ComputeVolumeDistribution(mesh);
        features.surfaceDistribution = ComputeSurfaceDistribution(mesh);
        
        // Shape histogram
        features.shapeHistogram = ComputeShapeHistogram(mesh, features.centroid);
        
        // Suggest category based on features
        features.suggestedCategory = SuggestCategory(features);
        
        Debug.Log($"Extracted features: {features}");
        
        return features;
    }

    /// <summary>
    /// Calculate the centroid of vertices
    /// </summary>
    private Vector3 CalculateCentroid(Vector3[] vertices)
    {
        Vector3 centroid = Vector3.zero;
        foreach (Vector3 v in vertices)
        {
            centroid += v;
        }
        return centroid / vertices.Length;
    }

    /// <summary>
    /// Compactness: ratio of surface area to volume
    /// Higher = more compact (sphere = most compact)
    /// </summary>
    private float CalculateCompactness(float surfaceArea, float volume)
    {
        if (volume <= 0) return 0;
        
        // Normalized by sphere's ratio
        float sphereRatio = Mathf.Pow(36f * Mathf.PI * volume * volume, 1f / 3f);
        return sphereRatio / surfaceArea;
    }

    /// <summary>
    /// Sphericity: how sphere-like the shape is
    /// </summary>
    private float CalculateSphericity(float surfaceArea, float volume)
    {
        if (surfaceArea <= 0) return 0;
        
        // Ratio of surface area of sphere with same volume to actual surface area
        float sphereSurfaceArea = Mathf.Pow(36f * Mathf.PI * volume * volume, 1f / 3f);
        return sphereSurfaceArea / surfaceArea;
    }

    /// <summary>
    /// Principal Component Analysis - finds main axes of the object
    /// Returns: (principal axes, eigenvalues)
    /// </summary>
    private (Vector3[], float[]) ComputePCA(Vector3[] vertices)
    {
        // Calculate centroid
        Vector3 centroid = CalculateCentroid(vertices);
        
        // Build covariance matrix
        float[,] covariance = new float[3, 3];
        
        foreach (Vector3 v in vertices)
        {
            Vector3 centered = v - centroid;
            covariance[0, 0] += centered.x * centered.x;
            covariance[0, 1] += centered.x * centered.y;
            covariance[0, 2] += centered.x * centered.z;
            covariance[1, 1] += centered.y * centered.y;
            covariance[1, 2] += centered.y * centered.z;
            covariance[2, 2] += centered.z * centered.z;
        }
        
        // Symmetrize
        covariance[1, 0] = covariance[0, 1];
        covariance[2, 0] = covariance[0, 2];
        covariance[2, 1] = covariance[1, 2];
        
        // Normalize
        int n = vertices.Length;
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
                covariance[i, j] /= n;
        
        // Simplified eigenvalue/eigenvector computation
        // For production, use a proper linear algebra library
        Vector3[] axes = new Vector3[3];
        float[] eigenvalues = new float[3];
        
        // Approximate principal axes using cross products and normalization
        // This is a simplified version - for production use proper eigen decomposition
        axes[0] = new Vector3(covariance[0, 0], covariance[1, 0], covariance[2, 0]).normalized;
        axes[1] = new Vector3(covariance[0, 1], covariance[1, 1], covariance[2, 1]).normalized;
        axes[2] = Vector3.Cross(axes[0], axes[1]).normalized;
        
        eigenvalues[0] = covariance[0, 0];
        eigenvalues[1] = covariance[1, 1];
        eigenvalues[2] = covariance[2, 2];
        
        return (axes, eigenvalues);
    }

    /// <summary>
    /// Compute volume distribution in 8 octants around centroid
    /// </summary>
    private float[] ComputeVolumeDistribution(Mesh mesh)
    {
        Vector3[] vertices = mesh.vertices;
        int[] triangles = mesh.triangles;
        Vector3 centroid = CalculateCentroid(vertices);
        
        float[] distribution = new float[8];
        
        for (int i = 0; i < triangles.Length; i += 3)
        {
            Vector3 v0 = vertices[triangles[i]];
            Vector3 v1 = vertices[triangles[i + 1]];
            Vector3 v2 = vertices[triangles[i + 2]];
            
            // Get triangle centroid
            Vector3 triCenter = (v0 + v1 + v2) / 3f;
            Vector3 relative = triCenter - centroid;
            
            // Determine octant (0-7)
            int octant = 0;
            if (relative.x > 0) octant += 1;
            if (relative.y > 0) octant += 2;
            if (relative.z > 0) octant += 4;
            
            // Add triangle's contribution (using area as proxy for volume)
            Vector3 edge1 = v1 - v0;
            Vector3 edge2 = v2 - v0;
            float area = Vector3.Cross(edge1, edge2).magnitude * 0.5f;
            
            distribution[octant] += area;
        }
        
        // Normalize
        float total = distribution.Sum();
        if (total > 0)
        {
            for (int i = 0; i < 8; i++)
                distribution[i] /= total;
        }
        
        return distribution;
    }

    /// <summary>
    /// Compute surface area distribution in 8 octants
    /// </summary>
    private float[] ComputeSurfaceDistribution(Mesh mesh)
    {
        // Similar to volume distribution but focuses on surface
        return ComputeVolumeDistribution(mesh);
    }

    /// <summary>
    /// Compute shape histogram based on distance from centroid
    /// Creates a "signature" of the shape
    /// </summary>
    private float[] ComputeShapeHistogram(Mesh mesh, Vector3 centroid)
    {
        const int bins = 32;
        float[] histogram = new float[bins];
        
        Vector3[] vertices = mesh.vertices;
        
        // Find max distance from centroid
        float maxDist = 0f;
        foreach (Vector3 v in vertices)
        {
            float dist = Vector3.Distance(v, centroid);
            if (dist > maxDist) maxDist = dist;
        }
        
        if (maxDist == 0) return histogram;
        
        // Build histogram
        foreach (Vector3 v in vertices)
        {
            float dist = Vector3.Distance(v, centroid);
            float normalized = dist / maxDist;
            int bin = Mathf.Min((int)(normalized * bins), bins - 1);
            histogram[bin]++;
        }
        
        // Normalize
        float total = vertices.Length;
        for (int i = 0; i < bins; i++)
        {
            histogram[i] /= total;
        }
        
        return histogram;
    }

    /// <summary>
    /// Suggest object category based on features (heuristic)
    /// </summary>
    private string SuggestCategory(MeshFeatures features)
    {
        Vector3 size = features.boundingBoxSize;
        float aspectXY = features.aspectRatioXY;
        float aspectYZ = features.aspectRatioYZ;
        
        // Washing machine heuristic: tall, boxy, aspect ratios close to 1
        if (size.y > 0.8f && size.y < 1.5f && 
            aspectXY > 0.6f && aspectXY < 1.4f &&
            features.compactness > 0.6f)
        {
            return "appliance_large";
        }
        
        // Flat/table-like
        if (aspectYZ < 0.3f && size.y < 0.5f)
        {
            return "furniture_table";
        }
        
        // Tall/chair-like
        if (aspectYZ > 2f && size.y > 0.6f)
        {
            return "furniture_chair";
        }
        
        // Very spherical
        if (features.sphericity > 0.8f)
        {
            return "object_spherical";
        }
        
        return "object_general";
    }

    /// <summary>
    /// Compare two feature sets and return similarity score (0-1, higher = more similar)
    /// </summary>
    public float CompareFeaturesSimple(MeshFeatures f1, MeshFeatures f2)
    {
        float score = 0f;
        int weights = 0;
        
        // Compare bounding box ratios (weight: 3)
        float sizeScore = 1f - Mathf.Abs(f1.aspectRatioXY - f2.aspectRatioXY) / 2f;
        sizeScore += 1f - Mathf.Abs(f1.aspectRatioXZ - f2.aspectRatioXZ) / 2f;
        sizeScore += 1f - Mathf.Abs(f1.aspectRatioYZ - f2.aspectRatioYZ) / 2f;
        score += sizeScore;
        weights += 3;
        
        // Compare compactness (weight: 2)
        float compactnessScore = 1f - Mathf.Abs(f1.compactness - f2.compactness);
        score += compactnessScore * 2;
        weights += 2;
        
        // Compare volume distribution (weight: 2)
        float distScore = 0f;
        for (int i = 0; i < 8; i++)
        {
            distScore += 1f - Mathf.Abs(f1.volumeDistribution[i] - f2.volumeDistribution[i]);
        }
        score += (distScore / 8f) * 2;
        weights += 2;
        
        // Compare shape histogram (weight: 3)
        float histScore = 0f;
        for (int i = 0; i < f1.shapeHistogram.Length; i++)
        {
            histScore += 1f - Mathf.Abs(f1.shapeHistogram[i] - f2.shapeHistogram[i]);
        }
        score += (histScore / f1.shapeHistogram.Length) * 3;
        weights += 3;
        
        return Mathf.Clamp01(score / weights);
    }
}