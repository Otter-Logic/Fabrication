using OtterLogic.StructuralEngine;

namespace OtterLogic.Fabrication;

/// <summary>
/// Every joint of a line model, each described by the same row of numbers — ready
/// for any clustering to find which joints are the same kind of connection.
/// </summary>
public sealed class JointSignatureResult
{
    internal JointSignatureResult(
        double[,] joints,
        double[,] signature,
        string[] featureNames,
        int[][] armLines,
        int[] handedness,
        bool[] supported,
        LineOrientation[] orientation,
        IReadOnlyList<string> notes)
    {
        Joints = joints;
        Signature = signature;
        FeatureNames = featureNames;
        ArmLines = armLines;
        Handedness = handedness;
        Supported = supported;
        Orientation = orientation;
        Notes = notes;
    }

    /// <summary>m x 3: every joint's position.</summary>
    public double[,] Joints { get; }

    /// <summary>Number of joints.</summary>
    public int JointCount => Joints.GetLength(0);

    /// <summary>
    /// m x f: one row per joint, in the order of <see cref="FeatureNames"/>. Unchanged
    /// by moving or turning a joint in plan, by the order its lines were drawn in, by
    /// mirroring it, and by whether a member passing through was drawn as one line or
    /// split at the joint.
    /// </summary>
    public double[,] Signature { get; }

    /// <summary>What each column of <see cref="Signature"/> measures.</summary>
    public string[] FeatureNames { get; }

    /// <summary>
    /// Per joint, the line behind each arm. A line passing through a joint appears
    /// twice, once for each side.
    /// </summary>
    public int[][] ArmLines { get; }

    /// <summary>
    /// Per joint, +1 or −1 for the two mirror images of an arrangement that has a
    /// handedness, 0 for one that is its own mirror image. Kept out of the signature
    /// so mirrored joints share a type, and reported here so the left- and
    /// right-hand versions can still be told apart.
    /// </summary>
    public int[] Handedness { get; }

    /// <summary>Whether each joint has a support.</summary>
    public bool[] Supported { get; }

    /// <summary>How each line stands, from the model's own spread of inclinations.</summary>
    public LineOrientation[] Orientation { get; }

    /// <summary>Anything that changed what was read, in plain words.</summary>
    public IReadOnlyList<string> Notes { get; }
}
