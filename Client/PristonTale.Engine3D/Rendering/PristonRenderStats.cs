namespace PristonTale.Engine3D.Rendering;

public readonly record struct PristonRenderStats(
    int TotalBatches,
    int VisibleBatches,
    int CulledBatches,
    int StaticBatches,
    int DynamicBatches,
    int DrawCalls,
    int Triangles,
    int StaticVertices,
    int StaticIndices,
    int NormalBatches,
    int TwoSidedBatches,
    int StateChanges,
    int TextureChanges,
    int VertexBufferChanges,
    int IndexBufferChanges,
    long DynamicVertexUploadBytes,
    PristonMeshDiagnostics VisibleStaticMeshDiagnostics,
    bool SunShadowUpdated,
    int LampShadowMapsUpdated,
    int ShadowDrawCalls);
