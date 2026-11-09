using Microsoft.Xna.Framework;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.Rendering;
using PristonTale.GltfExport;
using SharpGLTF.Schema2;
using PristonTale.Contracts.Motion;

namespace PristonTale.Tests;

internal static class GltfExportTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("glTF stage export preserves converted geometry, UVs, vertex alpha and alpha cutoff", StageAsync);
        yield return ("glTF rigid animation preserves authored key times", RigidAnimationAsync);
        yield return ("glTF actor export preserves skin bind data, multiple roots and exclusive clip end", ActorAsync);
        yield return ("glTF animation preserves original rotation-before-scale transforms", RotationBeforeScaleAsync);
        yield return ("glTF texture decoding follows the Engine3D decoder", ImageDecodeAsync);
        yield return ("glTF blend materials follow reference blend coverage and lighting", BlendMaterialAsync);
        yield return ("glTF T-pose raises biped arms laterally and carries shoulder guards", TPoseAsync);
        yield return ("glTF bakes sheared static node transforms into vertices", ShearedStaticNodeAsync);
    }

    private static Task ShearedStaticNodeAsync()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            File.WriteAllBytes(Path.Combine(directory, "shear.tga"), Tga(90, 90, 90));
            var shear = Matrix.Identity;
            shear.M21 = 0.75f;
            shear *= Matrix.CreateScale(2f, 1f, 1f) * Matrix.CreateFromAxisAngle(Vector3.Up, 0.6f) * Matrix.CreateTranslation(3f, 4f, 5f);
            var vertices = new[]
            {
                new PristonModelVertex(Vector3.Zero, Vector3.Forward),
                new PristonModelVertex(Vector3.UnitY, Vector3.Forward),
                new PristonModelVertex(Vector3.UnitX, Vector3.Forward)
            };
            var model = new PristonModel(
                "sheared.smd",
                [Material("shear.tga")],
                [ModelObject("Sheared", string.Empty, shear, vertices: vertices, faces: [new PristonModelFace(0, 1, 2, 0, 0)], textureCoordinates: [PristonTexLink.Empty])],
                [],
                new BoundingBox(Vector3.Zero, Vector3.One));
            var exported = GltfSceneExport.BuildModel(model, new MaterialFactory(directory, directory), new ExportOptions(false, false)).ToGltf2();
            var node = exported.LogicalNodes.Single(candidate => candidate.Name == "Sheared");
            var local = node.LocalMatrix;
            Assert.True(
                System.Numerics.Matrix4x4.Decompose(local, out var scale, out var rotation, out var translation)
                    && MatricesNear(
                        local,
                        System.Numerics.Matrix4x4.CreateScale(scale)
                            * System.Numerics.Matrix4x4.CreateFromQuaternion(rotation)
                            * System.Numerics.Matrix4x4.CreateTranslation(translation)),
                "sheared static node was written as a non-TRS matrix");
            var world = node.WorldMatrix;
            var positions = node.Mesh.Primitives.Single().GetVertexAccessor("POSITION").AsVector3Array()
                .Select(position => System.Numerics.Vector3.Transform(position, world))
                .ToArray();
            foreach (var vertex in vertices)
            {
                var expected = Vector3.Transform(vertex.Position, shear).ToNumerics();
                Assert.True(positions.Any(position => Near(position, expected)), "baking the shear moved a vertex");
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }

        return Task.CompletedTask;
    }

    private static Task TPoseAsync()
    {
        (string Name, string Parent, Vector3 Position)[] bones =
        [
            ("Bip01 Neck", "", new Vector3(0f, 37f, 0f)),
            ("Bip01 L Clavicle", "Bip01 Neck", new Vector3(1f, 37f, 0f)),
            ("Bip01 L UpperArm", "Bip01 L Clavicle", new Vector3(5f, 35f, 0f)),
            ("Bip01 L Forearm", "Bip01 L UpperArm", new Vector3(6f, 29f, 0f)),
            ("Bip01 L Hand", "Bip01 L Forearm", new Vector3(7f, 23f, 0f)),
            ("Bip01 L Finger1", "Bip01 L Hand", new Vector3(7f, 21f, 1f)),
            ("Bip01 bb01", "Bip01 L Clavicle", new Vector3(5f, 37f, 0f)),
            ("Bip01 bb02", "Bip01 bb01", new Vector3(6f, 31f, 0f)),
            ("Bip01 R Clavicle", "Bip01 Neck", new Vector3(-1f, 37f, 0f)),
            ("Bip01 R UpperArm", "Bip01 R Clavicle", new Vector3(-5f, 35f, 0f)),
            ("Bip01 R Forearm", "Bip01 R UpperArm", new Vector3(-6f, 29f, 0f)),
            ("Bip01 R Hand", "Bip01 R Forearm", new Vector3(-7f, 23f, 0f))
        ];
        var skeleton = new PristonModel(
            "biped.smb",
            [],
            bones.Select(bone => ModelObject(bone.Name, bone.Parent, Matrix.CreateTranslation(bone.Position))).ToArray(),
            [],
            new BoundingBox(Vector3.Zero, Vector3.One));
        var bind = bones.Select(static bone => Matrix.CreateTranslation(bone.Position)).ToArray();
        GltfSceneExport.ApplyTPose(skeleton, bind);

        Vector3 At(string name) => bind[Array.FindIndex(bones, bone => bone.Name == name)].Translation;
        Vector3 Direction(string from, string to) => Vector3.Normalize(At(to) - At(from));
        Assert.True(Vector3.Dot(Direction("Bip01 L UpperArm", "Bip01 L Forearm"), Vector3.UnitX) > 0.999f, "left upper arm is not lateral");
        Assert.True(Vector3.Dot(Direction("Bip01 L Forearm", "Bip01 L Hand"), Vector3.UnitX) > 0.999f, "left forearm is not lateral");
        Assert.True(Vector3.Dot(Direction("Bip01 R UpperArm", "Bip01 R Forearm"), -Vector3.UnitX) > 0.999f, "right upper arm is not lateral");
        Assert.True(Direction("Bip01 bb01", "Bip01 bb02").Y > -0.2f, "shoulder guard did not follow the raised arm");
        Assert.Near(35f, At("Bip01 L UpperArm").Y);
        return Task.CompletedTask;
    }

    private static Task BlendMaterialAsync()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            File.WriteAllBytes(Path.Combine(directory, "blend.bmp"), Bitmap32(60, 120, 200, 0));
            var alphaBlend = BakedPixel(directory, Material("blend.bmp") with { Transparency = 0.5f });
            var lamp = BakedPixel(directory, Material("blend.bmp") with { BlendType = 4 });
            var shadow = BakedPixel(directory, Material("blend.bmp") with { BlendType = 3 });
            var inverseShadow = BakedPixel(directory, Material("blend.bmp") with { BlendType = 6 });

            Assert.True(alphaBlend.SequenceEqual(new byte[] { 60, 120, 200, 255 }), "BLEND_ALPHA invented texture coverage");
            Assert.True(lamp.SequenceEqual(new byte[] { 60, 120, 200, 200 }), "LAMP coverage does not follow the texture colour");
            Assert.True(shadow.SequenceEqual(new byte[] { 0, 0, 0, 55 }), "SHADOW is not expressed as darkening coverage");
            Assert.True(inverseShadow.SequenceEqual(new byte[] { 0, 0, 0, 200 }), "INVSHADOW is not expressed as darkening coverage");

            var litFactory = new MaterialFactory(directory, directory);
            var unlitFactory = new MaterialFactory(directory, directory, unlit: true);
            var selfIllum = Material("blend.bmp") with { SelfIllum = 1f };
            var litMaterial = SingleMaterial(litFactory.Create(selfIllum, 0)!);
            var unlitMaterial = SingleMaterial(unlitFactory.Create(selfIllum, 0)!);

            Assert.True(!litMaterial.Unlit && litMaterial.FindChannel("Emissive")?.Texture is not null, "self-illuminated material lost its emission");
            Assert.True(unlitMaterial.Unlit, "stage materials must not be relit over their baked vertex lighting");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }

        return Task.CompletedTask;
    }

    private static byte[] BakedPixel(string assetDirectory, PristonMaterial material)
    {
        var pngDirectory = Path.Combine(assetDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(pngDirectory);
        new MaterialFactory(assetDirectory, pngDirectory).Create(material, 0);
        var png = Directory.GetFiles(pngDirectory, "*.png").Single();
        var image = ImageDecode.Decode(File.ReadAllBytes(png), ".png");
        return image.Rgba;
    }

    private static Material SingleMaterial(SharpGLTF.Materials.MaterialBuilder material)
    {
        var mesh = new SharpGLTF.Geometry.MeshBuilder<SharpGLTF.Geometry.VertexTypes.VertexPosition, SharpGLTF.Geometry.VertexTypes.VertexTexture1>("probe");
        mesh.UsePrimitive(material).AddTriangle(
            (new SharpGLTF.Geometry.VertexTypes.VertexPosition(0f, 0f, 0f), new SharpGLTF.Geometry.VertexTypes.VertexTexture1(System.Numerics.Vector2.Zero)),
            (new SharpGLTF.Geometry.VertexTypes.VertexPosition(1f, 0f, 0f), new SharpGLTF.Geometry.VertexTypes.VertexTexture1(System.Numerics.Vector2.UnitX)),
            (new SharpGLTF.Geometry.VertexTypes.VertexPosition(0f, 1f, 0f), new SharpGLTF.Geometry.VertexTypes.VertexTexture1(System.Numerics.Vector2.UnitY)));
        var scene = new SharpGLTF.Scenes.SceneBuilder();
        scene.AddRigidMesh(mesh, System.Numerics.Matrix4x4.Identity);
        return scene.ToGltf2().LogicalMaterials.Single();
    }

    private static Task RigidAnimationAsync()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            File.WriteAllBytes(Path.Combine(directory, "model.tga"), Tga(100, 120, 140));
            var modelObject = ModelObject(
                "Animated",
                string.Empty,
                Matrix.Identity,
                vertices:
                [
                    new PristonModelVertex(Vector3.Zero, Vector3.Forward),
                    new PristonModelVertex(Vector3.UnitY, Vector3.Forward),
                    new PristonModelVertex(Vector3.UnitX, Vector3.Forward)
                ],
                faces: [new PristonModelFace(0, 1, 2, 0, 0)],
                textureCoordinates: [PristonTexLink.Empty],
                positionKeys:
                [
                    new PristonPositionKey(0, Vector3.Zero),
                    new PristonPositionKey(100, new Vector3(10f, 0f, 0f)),
                    new PristonPositionKey(320, new Vector3(10f, 0f, 0f))
                ]);
            var model = new PristonModel(
                "model.smd",
                [Material("model.tga")],
                [modelObject],
                [],
                new BoundingBox(Vector3.Zero, Vector3.One));
            var factory = new MaterialFactory(directory, directory);
            var exported = GltfSceneExport.BuildModel(
                    model,
                    factory,
                    new ExportOptions(false, false))
                .ToGltf2();
            var animation = exported.LogicalAnimations.Single();
            var node = exported.LogicalNodes.Single(candidate => candidate.Name == "Animated");
            var keyTime = 100f / GltfSceneExport.TicksPerSecond;

            Assert.Near(10f, node.GetWorldMatrix(animation, keyTime).M41);

            var stageObject = GltfSceneExport.BuildModel(
                    model,
                    factory,
                    new ExportOptions(false, false, StageObject: true))
                .ToGltf2();
            Assert.Near(
                (model.LastFrame - 160) / GltfSceneExport.StageObjectTicksPerSecond,
                stageObject.LogicalAnimations.Single().Duration);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }

        return Task.CompletedTask;
    }

    private static Task StageAsync()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            File.WriteAllBytes(Path.Combine(directory, "stage.tga"), Tga(200, 180, 160));
            var material = Material("stage.tga");
            var vertices = new[]
            {
                new PristonStageVertex(new Vector3(1f, 2f, 3f), new Color(10, 20, 30, 64)),
                new PristonStageVertex(new Vector3(4f, 2f, 3f), Color.White),
                new PristonStageVertex(new Vector3(1f, 2f, 6f), Color.White)
            };
            var textureCoordinates = new PristonTexLink(
                new Vector2(0.1f, 0.2f),
                new Vector2(0.3f, 0.4f),
                new Vector2(0.5f, 0.6f),
                0);
            var stage = new PristonStage(
                "fixture.smd",
                [material],
                vertices,
                [new PristonStageFace(0, 1, 2, 0, 0)],
                [textureCoordinates],
                [],
                new BoundingBox(vertices[0].Position, vertices[2].Position));
            var factory = new MaterialFactory(directory, directory);
            var scene = GltfSceneExport.BuildStage(
                stage,
                factory,
                new ExportOptions(false, true));
            var model = scene.ToGltf2();
            var primitive = model.LogicalMeshes.Single().Primitives.Single();
            var positions = primitive.GetVertexAccessor("POSITION").AsVector3Array();
            var colors = primitive.GetVertexAccessor("COLOR_0").AsColorArray();
            var uvs = primitive.GetVertexAccessor("TEXCOORD_0").AsVector2Array();

            Assert.True(
                positions.Any(position => Near(position, new System.Numerics.Vector3(1f, 2f, 3f))),
                "the exporter applied a second coordinate-system conversion");
            Assert.True(
                colors.Any(color => MathF.Abs(color.W - 64f / 255f) <= 0.005f),
                "stage vertex alpha was discarded");
            Assert.True(
                uvs.Any(uv => Near(uv, new System.Numerics.Vector2(0.1f, 0.8f))),
                "--flip-v did not invert the V coordinate");
            Assert.Near(
                MaterialFactory.RuntimeAlphaCutoff,
                model.LogicalMaterials.Single().AlphaCutoff);

            var resolutionFactory = new MaterialFactory(directory, directory);
            resolutionFactory.Create(Material("stage.dds"), 1);
            var animatedMaterial = Material("missing.bmp") with
            {
                TextureType = 1,
                AnimationTextureNames = ["stage.tga"]
            };
            resolutionFactory.Create(animatedMaterial, 2);
            Assert.True(
                resolutionFactory.MissingTextures == 0,
                "texture extension fallback or animated-frame selection diverged from the runtime");

            File.WriteAllBytes(
                Path.Combine(directory, "shared.bmp"),
                Bitmap32(20, 40, 60, 0));
            var opaqueMaterial = resolutionFactory.Create(Material("shared.bmp"), 3);
            var animatedCutout = Material("unused.bmp") with
            {
                TextureType = 1,
                AnimationTextureNames = ["shared.bmp", "stage.tga"]
            };
            var maskMaterial = resolutionFactory.Create(animatedCutout, 4);
            Assert.True(
                !ReferenceEquals(opaqueMaterial, maskMaterial),
                "material cache merged distinct alpha modes");

            // A late-flagged cut-out stays a mask, as the engine draws it; a late colour blend stays blended.
            var lateCutout = Material("stage.tga") with { SurfaceFlags = PristonMaterialSurfaceFlags.RenderLast };
            Assert.True(resolutionFactory.Create(lateCutout, 5)!.AlphaMode == SharpGLTF.Materials.AlphaMode.MASK,
                "a late-flagged foliage cut-out exported as a blended material");
            Assert.True(resolutionFactory.Create(lateCutout with { BlendType = 4 }, 6)!.AlphaMode == SharpGLTF.Materials.AlphaMode.BLEND,
                "a late additive material exported without blending");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }

        return Task.CompletedTask;
    }

    private static Task ActorAsync()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            File.WriteAllBytes(Path.Combine(directory, "actor.tga"), Tga(120, 140, 160));
            var rootA = ModelObject(
                "RootA",
                string.Empty,
                Matrix.Identity,
                positionKeys:
                [
                    new PristonPositionKey(0, Vector3.Zero),
                    new PristonPositionKey(320, new Vector3(2f, 0f, 0f))
                ]);
            var rootB = ModelObject(
                "RootB",
                string.Empty,
                Matrix.CreateTranslation(0f, 5f, 0f));
            var skeleton = new PristonModel(
                "skeleton.smb",
                [],
                [rootA, rootB],
                [],
                new BoundingBox(Vector3.Zero, new Vector3(2f, 5f, 0f)));
            var meshObject = ModelObject(
                "Body",
                string.Empty,
                Matrix.Identity,
                hasPhysique: true,
                vertices:
                [
                    new PristonModelVertex(Vector3.Zero, Vector3.Forward),
                    new PristonModelVertex(Vector3.UnitY, Vector3.Forward),
                    new PristonModelVertex(Vector3.UnitX, Vector3.Forward)
                ],
                faces: [new PristonModelFace(0, 1, 2, 0, 0)],
                textureCoordinates: [PristonTexLink.Empty],
                physiqueBoneNames: ["RootA", "RootB", "RootA"]);
            var mesh = new PristonModel(
                "mesh.smd",
                [Material("actor.tga")],
                [meshObject],
                [],
                new BoundingBox(Vector3.Zero, Vector3.One));
            var actor = new PristonActor(
                ActorInfo(),
                mesh,
                skeleton,
                []);
            var factory = new MaterialFactory(
                directory,
                directory,
                forceDoubleSided: true);
            var scene = GltfSceneExport.BuildActor(
                actor,
                factory,
                new ExportOptions(false, false));
            var path = Path.Combine(directory, "actor.glb");
            scene.ToGltf2().SaveGLB(path);
            var model = ModelRoot.Load(path);
            var skin = model.LogicalSkins.Single();
            var animation = model.LogicalAnimations.Single();
            var rootNode = skin.Joints.Single(node => node.Name == "RootA");
            var expectedDuration = 320f / GltfSceneExport.TicksPerSecond;
            var expectedEndPosition = 2f * 319f / 320f;

            Assert.True(skin.JointsCount == 2, "the exported skin lost a skeleton joint");
            Assert.True(
                model.LogicalNodes.All(
                    static node => !(node.Name ?? string.Empty).EndsWith(
                        "__PT_SCALE_TRANSLATION",
                        StringComparison.Ordinal)),
                "uniform scale added redundant helper bones to the skeleton");
            Assert.True(
                skin.Joints.Select(static joint => joint.VisualRoot).Distinct().Count() == 1,
                "multiple skeleton roots were not joined under a common armature root");
            Assert.Near(expectedDuration, animation.Duration);
            Assert.Near(
                expectedEndPosition,
                rootNode.GetWorldMatrix(animation, animation.Duration).M41,
                0.0005f);

            var secondJoint = skin.GetJoint(1);
            var bindIdentity = secondJoint.InverseBindMatrix * secondJoint.Joint.WorldMatrix;
            Assert.True(
                MatrixNearIdentity(bindIdentity),
                "joint inverse bind matrix does not cancel its bind transform");

            var positions = model.LogicalMeshes.Single()
                .Primitives.Single()
                .GetVertexAccessor("POSITION")
                .AsVector3Array();
            Assert.True(
                positions.Any(position => Near(position, new System.Numerics.Vector3(0f, 6f, 0f))),
                "the skinned vertex was not baked into skeleton bind space");

            var animationsOnlyOptions = new ExportOptions(false, false, AnimationsOnly: true);
            var animationsOnly = GltfSceneExport.ToModel(
                GltfSceneExport.BuildActor(actor, factory, animationsOnlyOptions),
                animationsOnlyOptions);
            Assert.True(
                animationsOnly.LogicalMeshes.Count == 0 && animationsOnly.LogicalAnimations.Count == 1,
                "--anims-only must write the clips without geometry");
            var animationSkin = animationsOnly.LogicalSkins.Single();
            Assert.True(
                animationSkin.Joints.Select(static joint => joint.Name).Order().SequenceEqual(skin.Joints.Select(static joint => joint.Name).Order()),
                "the animation-only armature does not match the skinned export's joints");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }

        return Task.CompletedTask;
    }

    private static Task RotationBeforeScaleAsync()
    {
        var directory = CreateTemporaryDirectory();
        var rotation = Quaternion.CreateFromAxisAngle(Vector3.Up, MathHelper.PiOver4);
        var modelObject = ModelObject(
            "ScaledRotated",
            string.Empty,
            Matrix.Identity,
            vertices:
            [
                new PristonModelVertex(Vector3.Zero, Vector3.Forward),
                new PristonModelVertex(Vector3.UnitY, Vector3.Forward),
                new PristonModelVertex(Vector3.UnitX, Vector3.Forward)
            ],
            faces: [new PristonModelFace(0, 1, 2, 0, 0)],
            textureCoordinates: [PristonTexLink.Empty],
            rotationKeys:
            [
                new PristonRotationKey(0, rotation),
                new PristonRotationKey(160, rotation)
            ],
            scaleKeys:
            [
                new PristonScaleKey(0, new Vector3(2f, 1f, 1f)),
                new PristonScaleKey(160, new Vector3(2f, 1f, 1f))
            ]);
        var model = new PristonModel(
            "rotation-scale.smd",
            [Material("rotation-scale.tga")],
            [modelObject],
            [],
            default);
        try
        {
            File.WriteAllBytes(
                Path.Combine(directory, "rotation-scale.tga"),
                Tga(80, 100, 120));
            var factory = new MaterialFactory(directory, directory);
            var exported = GltfSceneExport.BuildModel(
                    model,
                    factory,
                    new ExportOptions(false, false))
                .ToGltf2();
            var animation = exported.LogicalAnimations.Single();
            var node = exported.LogicalNodes.Single(
                candidate => candidate.Name == "ScaledRotated");
            var expected = new PristonModelAnimator(model)
                .Evaluate(80)[0]
                .ToAffineNumerics();
            var actual = node.GetWorldMatrix(
                animation,
                80f / GltfSceneExport.TicksPerSecond);

            Assert.True(
                MatricesNear(expected, actual),
                "rotation-before-scale animation changed during glTF conversion");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }

        return Task.CompletedTask;
    }

    private static Task ImageDecodeAsync()
    {
        var bitmap = ImageDecode.Decode(Bitmap32(10, 20, 30, 0), ".bmp");
        Assert.True(bitmap.IsValid, "the shared decoder rejected a valid 32-bit BMP");
        Assert.True(!bitmap.HasSourceAlpha, "BMP alpha handling diverged from Engine3D");
        Assert.True(bitmap.Rgba[3] == 255, "the exporter preserved the unused BMP alpha byte");

        var original = Bitmap32(10, 20, 30, 0);
        BitConverter.GetBytes(0).CopyTo(original, 10);
        for (var index = 0; index < 14; index++)
        {
            original[index] = unchecked((byte)(original[index] + index * index));
        }

        original[0] = (byte)'A';
        original[1] = (byte)'8';
        var inferred = ImageDecode.Decode(original, ".bmp");
        Assert.True(
            inferred.IsValid && inferred.Rgba.Take(3).SequenceEqual(new byte[] { 10, 20, 30 }),
            "A8 BMPs with a zero bfOffBits must read pixels after the DIB header");

        var directory = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "source.png");
            PngWriter.Write(path, 1, 1, [40, 50, 60, 70]);
            var png = ImageDecode.Decode(File.ReadAllBytes(path), ".png");
            Assert.True(png.IsValid, "the exporter did not accept a PNG supported by Engine3D");
            Assert.True(
                png.Rgba.SequenceEqual(new byte[] { 40, 50, 60, 70 }),
                "PNG channels changed during shared decoding");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }

        return Task.CompletedTask;
    }

    private static PristonMaterial Material(string textureName)
    {
        return new PristonMaterial(
            true,
            1,
            0,
            1,
            false,
            Vector3.One,
            0f,
            0,
            PristonMaterialSurfaceFlags.None,
            textureName,
            string.Empty,
            [],
            [],
            0,
            0,
            0);
    }

    private static PristonModelObject ModelObject(
        string name,
        string parentName,
        Matrix transform,
        bool hasPhysique = false,
        IReadOnlyList<PristonModelVertex>? vertices = null,
        IReadOnlyList<PristonModelFace>? faces = null,
        IReadOnlyList<PristonTexLink>? textureCoordinates = null,
        IReadOnlyList<PristonRotationKey>? rotationKeys = null,
        IReadOnlyList<PristonPositionKey>? positionKeys = null,
        IReadOnlyList<PristonScaleKey>? scaleKeys = null,
        IReadOnlyList<string>? physiqueBoneNames = null)
    {
        return new PristonModelObject(
            name,
            parentName,
            hasPhysique,
            transform,
            transform.Translation,
            vertices ?? [],
            faces ?? [],
            textureCoordinates ?? [],
            rotationKeys ?? [],
            [],
            positionKeys ?? [],
            scaleKeys ?? [],
            physiqueBoneNames ?? [],
            [],
            [],
            []);
    }

    private static PristonModelInfo ActorInfo()
    {
        var motions = new PristonMotionInfo[PristonFormatConstants.CharacterMotionBase + 1];
        for (var index = 0; index < PristonFormatConstants.CharacterMotionBase; index++)
        {
            motions[index] = EmptyMotion();
        }

        motions[PristonFormatConstants.CharacterMotionBase] = new PristonMotionInfo(
            (uint)MotionCode.Stand,
            0,
            2,
            [],
            0,
            0,
            0,
            1,
            0,
            0);
        var emptyGroup = new PristonModelGroup(0, []);
        return new PristonModelInfo(
            "fixture.inx",
            "mesh.smd",
            "skeleton.smb",
            string.Empty,
            emptyGroup,
            emptyGroup,
            emptyGroup,
            motions,
            (uint)motions.Length,
            string.Empty,
            string.Empty,
            string.Empty,
            [],
            0);
    }

    private static PristonMotionInfo EmptyMotion()
    {
        return new PristonMotionInfo(
            0,
            0,
            0,
            [],
            0,
            0,
            0,
            0,
            0,
            0);
    }

    private static bool Near(
        System.Numerics.Vector3 left,
        System.Numerics.Vector3 right)
    {
        return System.Numerics.Vector3.DistanceSquared(left, right) <= 0.000001f;
    }

    private static bool Near(
        System.Numerics.Vector2 left,
        System.Numerics.Vector2 right)
    {
        return System.Numerics.Vector2.DistanceSquared(left, right) <= 0.000001f;
    }

    private static bool MatrixNearIdentity(System.Numerics.Matrix4x4 matrix)
    {
        return MathF.Abs(matrix.M11 - 1f) <= 0.0001f
            && MathF.Abs(matrix.M22 - 1f) <= 0.0001f
            && MathF.Abs(matrix.M33 - 1f) <= 0.0001f
            && MathF.Abs(matrix.M44 - 1f) <= 0.0001f
            && MathF.Abs(matrix.M12) <= 0.0001f
            && MathF.Abs(matrix.M13) <= 0.0001f
            && MathF.Abs(matrix.M14) <= 0.0001f
            && MathF.Abs(matrix.M21) <= 0.0001f
            && MathF.Abs(matrix.M23) <= 0.0001f
            && MathF.Abs(matrix.M24) <= 0.0001f
            && MathF.Abs(matrix.M31) <= 0.0001f
            && MathF.Abs(matrix.M32) <= 0.0001f
            && MathF.Abs(matrix.M34) <= 0.0001f
            && MathF.Abs(matrix.M41) <= 0.0001f
            && MathF.Abs(matrix.M42) <= 0.0001f
            && MathF.Abs(matrix.M43) <= 0.0001f;
    }

    private static bool MatricesNear(
        System.Numerics.Matrix4x4 left,
        System.Numerics.Matrix4x4 right)
    {
        if (!System.Numerics.Matrix4x4.Invert(right, out var inverseRight))
        {
            return false;
        }

        return MatrixNearIdentity(left * inverseRight);
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"priston-gltf-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static byte[] Tga(byte red, byte green, byte blue)
    {
        var bytes = new byte[21];
        bytes[2] = 2;
        bytes[12] = 1;
        bytes[14] = 1;
        bytes[16] = 24;
        bytes[17] = 0x20;
        bytes[18] = blue;
        bytes[19] = green;
        bytes[20] = red;
        return bytes;
    }

    private static byte[] Bitmap32(byte red, byte green, byte blue, byte alpha)
    {
        var bytes = new byte[58];
        bytes[0] = (byte)'B';
        bytes[1] = (byte)'M';
        BitConverter.GetBytes(bytes.Length).CopyTo(bytes, 2);
        BitConverter.GetBytes(54).CopyTo(bytes, 10);
        BitConverter.GetBytes(40).CopyTo(bytes, 14);
        BitConverter.GetBytes(1).CopyTo(bytes, 18);
        BitConverter.GetBytes(1).CopyTo(bytes, 22);
        BitConverter.GetBytes((ushort)1).CopyTo(bytes, 26);
        BitConverter.GetBytes((ushort)32).CopyTo(bytes, 28);
        BitConverter.GetBytes(4).CopyTo(bytes, 34);
        bytes[54] = blue;
        bytes[55] = green;
        bytes[56] = red;
        bytes[57] = alpha;
        return bytes;
    }
}
