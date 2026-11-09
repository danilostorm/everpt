> **Issue note for the owner, not a pull request.** MonoGame's CONTRIBUTING.md ("Large Language Models /
> Generative AI") prohibits contributions made with generative AI, and this text was written by an AI agent. So it
> cannot be sent upstream as it is. What can go upstream is an issue describing the problem, written by the owner, or
> a fix the owner (or another human) writes independently. The facts below are the material for that.

# Setting one element of an array effect parameter does not reach the shader

### Problem

`ConstantBuffer.Update` (`MonoGame.Framework/Graphics/Shader/ConstantBuffer.cs`) copies a parameter into the buffer
only when that parameter's `StateKey` moved since the last update:

```csharp
if (param.StateKey < _stateKey)
    continue;
```

- `EffectParameter.Elements[i].SetValue(...)` stores the element's value and moves the element's `StateKey`. The
  array parameter's key does not move.
- The check above looks at the array parameter only, so the new value is not copied.
- It shows up only when the whole array is written again, for example on the first update or after a `SetValue` of
  the array itself.

The code is in the shared C#, not in a backend. It was found on Native/Vulkan:
- a material's six UV transforms, a `float4 UvU[6]` array, were set per element;
- they stayed at zero, so every texture was sampled at UV 0.

### Workaround in application code

The whole array is set with `parameter.SetValue(Vector4[])` from a preallocated array. That moves the parent's
`StateKey`.

### Possible fix

Any one of these:
- when an element changes, also move its parent's `StateKey` (the element needs a reference to its parent);
- have `ConstantBuffer.Update` check the elements' keys for array parameters.

Repro: an effect with `float4 Values[2]`. Draw once, then call
`effect.Parameters["Values"].Elements[1].SetValue(Vector4.One)` and draw again: the shader still reads the old value.
