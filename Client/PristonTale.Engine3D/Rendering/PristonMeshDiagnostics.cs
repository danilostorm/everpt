namespace PristonTale.Engine3D.Rendering;

public readonly record struct PristonMeshDiagnostics(
    int DegenerateTriangles,
    int UpFacingTriangles,
    int DownFacingTriangles,
    int SideFacingTriangles)
{
    public static PristonMeshDiagnostics operator +(PristonMeshDiagnostics left, PristonMeshDiagnostics right) =>
        new(
            left.DegenerateTriangles + right.DegenerateTriangles,
            left.UpFacingTriangles + right.UpFacingTriangles,
            left.DownFacingTriangles + right.DownFacingTriangles,
            left.SideFacingTriangles + right.SideFacingTriangles);
}
