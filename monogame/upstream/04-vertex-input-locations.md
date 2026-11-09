> **Issue note for the owner, not a pull request.** MonoGame's CONTRIBUTING.md ("Large Language Models /
> Generative AI") prohibits contributions made with generative AI, and this patch and text were written by an AI
> agent. So they cannot be sent upstream as they are. What can go upstream is an issue describing the problem,
> written by the owner, or a fix the owner (or another human) writes independently. The facts below are the material
> for that.

# [Vulkan] Vertex attributes land on the wrong locations when a vertex shader leaves an input unread

### Problem

`MGG_InputLayout_Create` gives the i-th input element `location = i`.
- The C# side (`VertexInputLayout.GenerateInputElements`) builds the elements in the order of the shader's reflected
  attributes.
- mgfxc (`ShaderProfile.Vulkan.cs`) reflects only the inputs the shader reads.
- DXC removes an unread input from the entry point but keeps the locations of the others.

So the i-th reflected attribute is not always at location i. Example: a depth-only vertex shader that reads the
position and the UVs of the full vertex struct.

```hlsl
struct VSIn { float4 Position : POSITION0; float3 Normal : NORMAL0; float4 Tangent : TANGENT0;
              float2 Uv0 : TEXCOORD0; float2 Uv1 : TEXCOORD1; };
VSOut VS(VSIn i) { /* reads Position, Uv0 and Uv1 only */ }
```

- The SPIR-V inputs are `in.var.POSITION0` at location 0, `in.var.TEXCOORD0` at 3 and `in.var.TEXCOORD1` at 4.
- mgfxc reflects three attributes, and the runtime puts them at locations 0, 1 and 2.
- Locations 3 and 4 get no attribute: `VUID-VkGraphicsPipelineCreateInfo-Input-07904` ("does not have a Location 3
  but vertex shader has an input variable at that Location"), and the UVs the shader reads are undefined.
- Alpha-tested shadow casters that share their vertex struct with the lit pass hit this.
- The same effect works on DesktopGL, which binds attributes by name.

### The fix in the fork

`MGG_Shader_Create` reads the location of every vertex input from the SPIR-V: each `OpVariable` with storage class
`Input`, its `Location` decoration and its semantic.
- The semantic comes from the `UserSemantic` decoration when the shader was compiled with `-fspv-reflect`, else from
  DXC's `in.var.<SEMANTIC><n>` name.
- Semantic names map to `VertexElementUsage` as in mgfxc.

`MGG_InputLayout_Create` then gives each element the location of the input with its usage and index. An input that
spans several locations (a matrix) is found by its first index. When nothing matches, the element keeps
`location = i`, the previous behaviour.

There is an alternative fix in mgfxc that needs no SPIR-V parsing at run time: write each attribute's real location
into `ShaderData.Attribute.location` (reserved for this, always 0 today) and pass it through `MGG_InputElement`. It
changes the effect format and the interop struct.

Repro: compile the shader above with the Vulkan profile. Draw it with a vertex buffer that holds all five elements,
with `VK_INSTANCE_LAYERS=VK_LAYER_KHRONOS_validation`.
- With the fix the error is gone.
- The image is unchanged against a shader that declares only the inputs it reads (a test scene: mean difference
  0.33 levels, p99 2, which is the TAA noise between runs).
