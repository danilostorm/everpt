using Microsoft.Xna.Framework.Graphics;

namespace PristonTale.Engine3D.Rendering;

internal sealed class PristonRenderStateCache
{
    private readonly GraphicsDevice _graphicsDevice;
    private BlendState _blendState;
    private DepthStencilState _depthStencilState;
    private RasterizerState _rasterizerState;
    private SamplerState _samplerState0;
    private VertexBuffer _vertexBuffer;
    private IndexBuffer _indexBuffer;

    public PristonRenderStateCache(GraphicsDevice graphicsDevice)
    {
        _graphicsDevice = graphicsDevice;
    }

    public int BlendStateChanges { get; private set; }
    public int DepthStateChanges { get; private set; }
    public int RasterizerStateChanges { get; private set; }
    public int SamplerStateChanges { get; private set; }
    public int VertexBufferChanges { get; private set; }
    public int IndexBufferChanges { get; private set; }

    // Texture bindings go through effect parameters and are counted by PristonShaderLibrary.
    public int TotalStateChanges =>
        BlendStateChanges +
        DepthStateChanges +
        RasterizerStateChanges +
        SamplerStateChanges +
        VertexBufferChanges +
        IndexBufferChanges;

    public void BeginFrame()
    {
        BlendStateChanges = 0;
        DepthStateChanges = 0;
        RasterizerStateChanges = 0;
        SamplerStateChanges = 0;
        VertexBufferChanges = 0;
        IndexBufferChanges = 0;
    }

    public void InvalidateDeviceState()
    {
        _blendState = null;
        _depthStencilState = null;
        _rasterizerState = null;
        _samplerState0 = null;
        _vertexBuffer = null;
        _indexBuffer = null;
    }

    public void SetBlendState(BlendState state)
    {
        if (ReferenceEquals(_blendState, state))
            return;

        _graphicsDevice.BlendState = state;
        _blendState = state;
        BlendStateChanges++;
    }

    public void SetDepthStencilState(DepthStencilState state)
    {
        if (ReferenceEquals(_depthStencilState, state))
            return;

        _graphicsDevice.DepthStencilState = state;
        _depthStencilState = state;
        DepthStateChanges++;
    }

    public void SetRasterizerState(RasterizerState state)
    {
        if (ReferenceEquals(_rasterizerState, state))
            return;

        _graphicsDevice.RasterizerState = state;
        _rasterizerState = state;
        RasterizerStateChanges++;
    }

    public void SetSamplerState0(SamplerState state)
    {
        if (ReferenceEquals(_samplerState0, state))
            return;

        _graphicsDevice.SamplerStates[0] = state;
        _samplerState0 = state;
        SamplerStateChanges++;
    }

    public void SetVertexBuffer(VertexBuffer vertexBuffer)
    {
        if (ReferenceEquals(_vertexBuffer, vertexBuffer))
            return;

        _graphicsDevice.SetVertexBuffer(vertexBuffer);
        _vertexBuffer = vertexBuffer;
        VertexBufferChanges++;
    }

    public void SetIndexBuffer(IndexBuffer indexBuffer)
    {
        if (ReferenceEquals(_indexBuffer, indexBuffer))
            return;

        _graphicsDevice.Indices = indexBuffer;
        _indexBuffer = indexBuffer;
        IndexBufferChanges++;
    }
}
