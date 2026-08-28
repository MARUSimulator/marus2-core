using UnityEngine;

/// <summary>
/// Pointcloud object
/// </summary>
public class PointCloud
{
    /// <summary>
    /// Holds point data (x, y, z)
    /// </summary>
    public Vector3[] Points;
    /// <summary>
    /// Holds normals data (x, y, z)
    /// </summary>
    public Vector3[] Normals = null;

    /// <summary>
    /// Holds color data
    /// </summary>
    public Color[] Colors = null;

    /// <summary>
    /// Number of points
    /// </summary>
    public int Length;

    public PointCloud()
    {

    }

    public PointCloud(Vector3[] points)
    {
        this.Points = points;
    }


    public PointCloud(Vector3[] points, Color[] colors) : this(points)
    {
        this.Colors = colors;
    }

    public PointCloud(Vector3[] points, Color[] colors, Vector3[] normals) : this(points, colors)
    {
        this.Normals = normals;
    }

    public PointCloud(Vector3[] points, Vector3[] normals) : this(points)
    {
        this.Normals = normals;
    }
}