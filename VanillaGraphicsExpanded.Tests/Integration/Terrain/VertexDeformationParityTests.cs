using System.Buffers.Binary;
using System.Runtime.InteropServices;
using TinyTokenizer.Ast;
using ShaderBuildTool.Spirv;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Tests.Integration.Support.Rendering.Shaders;
using VanillaGraphicsExpanded.Tests.Support.GPU.Native;
using VanillaGraphicsExpanded.Tests.Support.GPU.Shaders;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Integration.Terrain;

/// <summary>Compares installed visible material stages with their offline-precompiled shadow material producers.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class VertexDeformationParityTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Visible and static shadow producers publish matching camera-relative positions for each suppression mode.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void InstalledVisibleAndShadowPositionsMatch(int dontWarpVertices)
    {
        // Arrange: adapt only the installed source's assigned uniforms into an RHI-bound test block.
        EnsureContextValid();
        var commands = RHICommandContext.Current;
        using var visible = CreateVisible(InstalledEngineShaderSources.Build("standard.vsh", 0, 0, 0, 0, 0),
            "#define WAVINGSTUFF 1\n", dontWarpVertices,
            [2.5f, -3.25f, 4.75f], [0, 0, 0]);
        using var shadow = BuiltShaderFixture.LoadProgram(new(new ShaderSettings(
            TestShaderPrograms.Create().Programs["tests/shadow-static-feedback"])));
        using var shadowMaterial = CreateUniform(MaterialInputs([2.5f, -3.25f, 4.75f], [0, 0, 0]));
        using var shadowFlags = CreateUniform(ObjectFlags(dontWarpVertices));
        var shadowBindings = Bindings(shadow,
            ("VgeShadowMaterial", shadowMaterial), ("VgeShadowObjectFlags", shadowFlags));
        using var geometry = CreatePointGeometry([13.25f, -4.5f, 21.75f], 1 << 25, 0);
        using var target = CreateRenderTarget(1, 1, TextureFormat.Rgba8);

        // Act: execute each real producer through RHI graphics, feedback, and readback commands.
        float[] visiblePosition = CapturePosition(visible.Shader, visible.Bindings, geometry.Inputs, target);
        float[] shadowPosition = CapturePosition(shadow, shadowBindings, geometry.Inputs, target);

        // Assert: identity shadow projection leaves output in the same camera-relative world space.
        AssertPositionsMatch(visiblePosition, shadowPosition);
        if (dontWarpVertices == 1)
        {
            Assert.InRange(MathF.Abs(visiblePosition[0] - 13.25f - 2.5f), 0f, 2e-5f);
            Assert.InRange(MathF.Abs(visiblePosition[1] + 4.5f + 3.25f), 0f, 2e-5f);
            Assert.InRange(MathF.Abs(visiblePosition[2] - 21.75f - 4.75f), 0f, 2e-5f);
        }
        else AssertMotion(visiblePosition, [13.25f, -4.5f, 21.75f], [2.5f, -3.25f, 4.75f], [0, 0, 0]);
    }

    /// <summary>Visible entity animation and the precompiled shadow skinning producer preserve skin and object placement.</summary>
    [Fact]
    public void InstalledEntityAndShadowPositionsMatchAfterSkinning()
    {
        // Arrange: use a nonidentity joint zero transform in both real shader paths.
        EnsureContextValid();
        using var visible = CreateVisible(InstalledEngineShaderSources.Build("entityanimated.vsh", 0, 0, 0, 0, 0),
            "#define WAVINGSTUFF 1\n", 0,
            [2.5f, -3.25f, 4.75f], [0, 0, 0], animation: true);
        using var shadow = BuiltShaderFixture.LoadProgram(new(new ShaderSettings(
            TestShaderPrograms.Create().Programs["tests/shadow-entity-feedback"])));
        using var shadowMaterial = CreateUniform(MaterialInputs([2.5f, -3.25f, 4.75f], [0, 0, 0]));
        using var animation = CreateUniform(Animation([.5f, .75f, -1.25f]));
        using var flags = CreateUniform(new byte[16]);
        var shadowBindings = Bindings(shadow,
            ("VgeShadowMaterial", shadowMaterial), ("VgeShadowAnimation", animation), ("VgeShadowEntityFlags", flags));
        using var geometry = CreatePointGeometry([5.25f, -2.5f, 9.75f], 1 << 25, 0);
        using var target = CreateRenderTarget(1, 1, TextureFormat.Rgba8);

        // Act: run the installed animated material and precompiled shadow producer on the same skinned vertex.
        float[] actualVisible = CapturePosition(visible.Shader, visible.Bindings, geometry.Inputs, target);
        float[] actualShadow = CapturePosition(shadow, shadowBindings, geometry.Inputs, target);

        // Assert: skinning occurs before shared motion, and both producers publish the same position.
        AssertPositionsMatch(actualVisible, actualShadow);
        AssertMotion(actualVisible, [5.25f, -2.5f, 9.75f], [2.5f, -3.25f, 4.75f], [.5f, .75f, -1.25f]);
    }

    /// <summary>Visible terrain and precompiled terrain shadow producers apply chunk placement before shared deformation.</summary>
    [Fact]
    public void InstalledTerrainAndShadowPositionsMatchAfterChunkPlacement()
    {
        // Arrange: pair the installed ordinary terrain stage with the authored shadow terrain decoder.
        EnsureContextValid();
        using var visible = CreateVisible(InstalledEngineShaderSources.Build("chunkopaque.vsh", 0, 0, 0, 0, 0),
            "#define WAVINGSTUFF 1\n", 0,
            [0, 0, 0], [3.5f, -2.25f, 5.75f]);
        using var shadow = BuiltShaderFixture.LoadProgram(new(new ShaderSettings(
            TestShaderPrograms.Create().Programs["tests/shadow-terrain-feedback"])));
        using var shadowMaterial = CreateUniform(MaterialInputs([0, 0, 0], [3.5f, -2.25f, 5.75f]));
        var shadowBindings = Bindings(shadow, ("VgeShadowMaterial", shadowMaterial));
        using var geometry = CreatePointGeometry([8.25f, -6.5f, 14.75f], 1 << 25, 0, terrain: true);
        using var target = CreateRenderTarget(1, 1, TextureFormat.Rgba8);

        // Act: execute each actual position producer with the same chunk origin and deformation state.
        float[] actualVisible = CapturePosition(visible.Shader, visible.Bindings, geometry.Inputs, target);
        float[] actualShadow = CapturePosition(shadow, shadowBindings, geometry.Inputs, target);

        // Assert: camera-relative terrain positions match after placement and shared material motion.
        AssertPositionsMatch(actualVisible, actualShadow);
        AssertMotion(actualVisible, [8.25f, -6.5f, 14.75f], [3.5f, -2.25f, 5.75f], [0, 0, 0]);
    }
    #endregion

    #region Private
    /// <summary>Captures one vertex producer through an RHI pipeline, feedback owner and bounded buffer readback.</summary>
    private static float[] CapturePosition(RHIShader shader, RHIResourceBindings bindings,
        RHIGeometryInputs geometry, GpuFramebuffer target)
    {
        var commands = RHICommandContext.Current;
        var pass = FramebufferTestSetup.DescribeAttachments(target);
        using var pipeline = commands.CreateGraphicsPipeline(new(shader, geometry.Layout,
            commands.DescribeRenderTargets(pass), DynamicPipelineState.Viewport,
            rasterizer: new() { Discard = true }, assembly: new() { Topology = PrimitiveTopology.Points },
            label: "Tests.VertexDeformationParity")).RequireResource();
        using var output = commands.CreateBuffer(new RHIBufferDescription
        {
            Role = BufferRole.TransformFeedback, Mode = ResourceStorageMode.Mutable, Capacity = 16,
            Usage = BufferUsage.StreamRead, Label = "Tests.VertexDeformationParity.Output"
        }).RequireResource();
        using var capture = commands.CreateTransformFeedback(new([new(0, new(output, 0, 16))])).RequireResource();
        using (var boundary = commands.EnterBoundary(new("Tests.VertexDeformationParity", true,
            graphics: [pipeline], operations: BoundaryOperations.RenderPasses)))
        {
            boundary.Run(() =>
            {
                commands.BeginRenderPass(pass);
                commands.SetPipeline(pipeline);
                commands.SetDynamicState(new() { Viewport = commands.PassViewport });
                commands.BindResources(bindings);
                using (commands.CaptureTransformFeedback(capture, CapturePrimitive.Points))
                    commands.Draw(geometry, new(0, 1));
                commands.EndRenderPass();
            });
        }

        commands.Barrier(new(ResourceAccess.BufferUpdate));
        using var readback = commands.EnqueueReadback(new(new RHIBufferRange(output, 0, 16)));
        Assert.Equal(CompletionStatus.Ready, readback.Wait());
        using var mapped = readback.MapReadyReadback();
        return MemoryMarshal.Cast<byte, float>(mapped.Bytes).ToArray();
    }

    /// <summary>Prepares the installed visible vertex stage with an AST-authored feedback output and explicit test UBO.</summary>
    private static VisibleProgram CreateVisible(string source, string defines, int dontWarpVertices,
        float[] modelTranslation, float[] origin, bool animation = false)
    {
        string preprocessed = AddDefines(source, defines);
        string prepared = AdaptVisibleStage(preprocessed);
        var contract = new GpuBindingContract();
        contract.RegisterUniformBlockBinding("VgeParityInputs", 0);
        var blockNames = SyntaxTree.Parse(prepared, GlslSchema.Instance)
            .Select(Query.Syntax<GlInterfaceBlockHeaderNode>()).Cast<GlInterfaceBlockHeaderNode>()
            .Where(header => header.Storage == GlInterfaceStorage.Uniform).Select(header => header.Name)
            .ToHashSet(StringComparer.Ordinal);
        if (blockNames.Contains("VgeFrameUBO"))
            contract.RegisterUniformBlockBinding("VgeFrameUBO", GpuBindingRegistry.Ubo.Frame);
        if (blockNames.Contains("Animation")) contract.RegisterUniformBlockBinding("Animation", 1);
        if (animation != blockNames.Contains("Animation"))
            throw new InvalidDataException("The visible parity shader's animation block did not match the requested entity variant.");
        var reliefSamplers = SyntaxTree.Parse(prepared, GlslSchema.Instance)
            .Select(Query.Syntax<GlUniformNode>()).Cast<GlUniformNode>()
            .Where(uniform => TerrainReliefSamplerUnits.ContainsKey(uniform.Name)).ToArray();
        foreach (var sampler in reliefSamplers)
            contract.RegisterSamplerUnit(sampler.Name, TerrainReliefSamplerUnits[sampler.Name]);
        var commands = RHICommandContext.Current;
        var ownedResources = new List<IDisposable>();
        RHIShader? shader = null;
        try
        {
            shader = commands.CreateShader(new(ShaderKind.Graphics,
                [new(ShaderStage.Vertex, prepared)], contract, label: "Tests.VertexDeformationParity.Visible"))
                .RequireResource();
            var values = VisibleInputs(dontWarpVertices, modelTranslation, origin);
            var uniformBindings = new List<RHIUniformBinding>();
            var sampledBindings = new List<RHISamplerBinding>();
            var inputBuffer = CreateUniform(values);
            ownedResources.Add(inputBuffer);
            var bindings = new List<RHIBufferBinding>
            {
                new(GpuBindingEntry.Identity(ShaderBindingKind.UniformBlock, "VgeParityInputs"), BufferRole.Uniform,
                    inputBuffer)
            };
            if (blockNames.Contains("VgeFrameUBO"))
            {
                var frame = new VgeFrameUniformBuffer();
                ownedResources.Add(frame);
                uniformBindings.Add(new(GpuBindingEntry.Identity(ShaderBindingKind.UniformBlock, "VgeFrameUBO"), frame.Capture()));
            }
            if (blockNames.Contains("Animation"))
            {
                var animationBuffer = CreateUniform(Animation([.5f, .75f, -1.25f]));
                ownedResources.Add(animationBuffer);
                bindings.Add(new(GpuBindingEntry.Identity(ShaderBindingKind.UniformBlock, "Animation"), BufferRole.Uniform,
                    animationBuffer));
            }
            if (reliefSamplers.Length > 0)
            {
                float[] zero = [0, 0, 0, 0];
                var texture = commands.CreateTexture2D(new RHITextureDescription
                {
                    Dimension = TextureDimension.Two, Width = 1, Height = 1, Format = TextureFormat.Rgba32f,
                    MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest,
                    BaseLevel = 0, MaxLevel = 0, Label = "Tests.VertexDeformationParity.NeutralDisplacement"
                }, [new RHITextureInitialData(new(new(), 0, 0, 0, 1, 1),
                    new RHIPixelEncoding(PixelChannelLayout.Rgba, PixelComponentType.Float),
                    MemoryMarshal.AsBytes(zero.AsSpan()).ToArray())]).RequireResource();
                ownedResources.Add(texture);
                foreach (var sampler in reliefSamplers)
                    sampledBindings.Add(new(GpuBindingEntry.Identity(ShaderBindingKind.Sampler, sampler.Name),
                        texture, Sampler: GpuSamplers.NearestClamp));
            }
            return new(shader, new(shader, samplers: sampledBindings, buffers: bindings, uniforms: uniformBindings), ownedResources);
        }
        catch
        {
            foreach (var resource in ownedResources) resource.Dispose();
            shader?.Dispose();
            throw;
        }
    }

    /// <summary>Moves only known native standalone inputs into a test UBO and decorates the parsed world-position output.</summary>
    private static string AdaptVisibleStage(string source)
    {
        var tree = SyntaxTree.Parse(source, GlslSchema.Instance);
        var fields = VisibleUniformFields;
        var arrayFields = VisibleUniformArrayFields;
        var allUniforms = tree.Select(Query.Syntax<GlUniformNode>()).Cast<GlUniformNode>().ToArray();
        var uniforms = allUniforms.Where(node => fields.ContainsKey(node.Name) || arrayFields.ContainsKey(node.Name)).ToArray();
        var reliefUniforms = allUniforms.Where(node => TerrainReliefSamplerUnits.ContainsKey(node.Name)).ToArray();
        if (reliefUniforms.Any(node => node.TypeName != "sampler2D" || node.IsArray))
            throw new InvalidDataException("A terrain relief input changed its expected scalar sampler2D shape.");
        if (uniforms.Length == 0) throw new InvalidDataException("The visible parity shader has no supported standalone deformation inputs.");
        foreach (var group in uniforms.GroupBy(node => node.Name, StringComparer.Ordinal))
        {
            if (fields.TryGetValue(group.Key, out var scalarField))
            {
                if (group.Any(node => node.TypeName != scalarField.Type || node.IsArray))
                    throw new InvalidDataException($"Visible parity input '{group.Key}' changed its declared type.");
                continue;
            }

            var arrayField = arrayFields[group.Key];
            foreach (var node in group)
            {
                var extentNodes = node.Children.OfType<SyntaxBlock>()
                    .Single(block => block.Kind == NodeKind.BracketBlock).InnerChildren.ToArray();
                if (node.TypeName != arrayField.Type || !node.IsArray || node.InitializerNodes.Any() ||
                    !extentNodes.Select(static extentNode => extentNode is SyntaxToken token ? token.Text : string.Empty)
                        .SequenceEqual(arrayField.ExtentTokens))
                    throw new InvalidDataException($"Visible parity array input '{group.Key}' changed its declared type or extent.");
            }
        }
        ValidateDynamicLightDefine(tree, uniforms.Any(node => arrayFields.TryGetValue(node.Name, out var arrayField) &&
            arrayField.ExtentTokens.SequenceEqual(["DYNLIGHTS"])));
        var moved = uniforms.Select(node => node.Name).Distinct(StringComparer.Ordinal).ToArray();
        string members = string.Join("\n", moved.OrderBy(name => fields.TryGetValue(name, out var scalar) ? scalar.Offset : arrayFields[name].Offset)
            .Select(name => fields.TryGetValue(name, out var scalar)
                ? $"    layout(offset={scalar.Offset}) {scalar.Type} {name};"
                : $"    layout(offset={arrayFields[name].Offset}) {arrayFields[name].Type} {name}[{arrayFields[name].Extent}];"));
        var unboundUniforms = allUniforms.Where(node => !fields.ContainsKey(node.Name) && !arrayFields.ContainsKey(node.Name) &&
            !TerrainReliefSamplerUnits.ContainsKey(node.Name)).ToArray();
        var numericTypes = GlslKeywords.ScalarTypes.Concat(GlslKeywords.VectorTypes)
            .Concat(GlslKeywords.MatrixTypes).ToHashSet(StringComparer.Ordinal);
        foreach (var uniform in unboundUniforms)
        {
            if (uniform.IsArray || !numericTypes.Contains(uniform.TypeName))
                throw new InvalidDataException($"Visible parity shader has unsupported unbound uniform '{uniform.Name}' of type '{uniform.TypeName}'.");
        }
        var blockHeaders = tree.Select(Query.Syntax<GlInterfaceBlockHeaderNode>()).Cast<GlInterfaceBlockHeaderNode>().ToArray();
        if (blockHeaders.Any(header => header.Storage == GlInterfaceStorage.Uniform && header.Name == "VgeParityInputs"))
            throw new InvalidDataException("The installed shader already owns the parity fixture block name.");
        var outputs = tree.Select(Query.Syntax<GlStageIoNode>().Named("worldPos")).Cast<GlStageIoNode>()
            .Where(node => node.Storage == GlInterfaceStorage.Output && node.ValueType == "vec4").ToArray();
        if (outputs.Length == 0) throw new InvalidDataException("The installed visible stage has no vec4 worldPos output.");
        var animationHeaders = tree.Select(Query.Syntax<GlInterfaceBlockHeaderNode>()).Cast<GlInterfaceBlockHeaderNode>()
            .Where(header => header.Storage == GlInterfaceStorage.Uniform && header.Name == "Animation").ToArray();
        GlLayoutNode? animationLayout = null;
        if (animationHeaders.Length > 1)
            throw new InvalidDataException("The installed visible stage declares Animation more than once.");
        if (animationHeaders.Length == 1)
        {
            animationLayout = tree.Select(Query.Syntax<GlLayoutNode>()).Cast<GlLayoutNode>()
                .Where(layout => layout.EndPosition <= animationHeaders[0].Position)
                .OrderByDescending(layout => layout.Position).FirstOrDefault();
            if (animationLayout is null || !animationLayout.Qualifiers.Any(qualifier =>
                    qualifier.Nodes.OfType<SyntaxToken>().Any(token => token.Text == "std140")) ||
                animationLayout.Qualifiers.Any(qualifier => qualifier.Kind == GlLayoutQualifierKind.Binding))
                throw new InvalidDataException("The installed Animation block is missing its expected unbound std140 layout.");
        }
        var version = tree.Select(Query.Syntax<GlDirectiveNode>().Named("version")).Cast<GlDirectiveNode>().Single();
        // This fixture compiles as GLSL 450, where these explicitly supported extensions are core features.
        // Drop only those parsed directives; reject other requests so the adaptation cannot change unknown semantics.
        var extensions = tree.Select(Query.Syntax<GlDirectiveNode>().Named("extension"))
            .Cast<GlDirectiveNode>().ToArray();
        var coreExtensions = new HashSet<string>(StringComparer.Ordinal)
        {
            "GL_ARB_explicit_attrib_location", "GL_ARB_separate_shader_objects",
            "GL_ARB_shading_language_420pack", "GL_ARB_shader_image_load_store",
            "GL_ARB_shader_storage_buffer_object"
        };
        foreach (var directive in extensions)
        {
            // Preserve every parsed argument node during validation; an unexpected grouped node
            // must not disappear while extracting token operands.
            SyntaxNode[] argumentNodes = directive.Arguments.ToArray();
            if (argumentNodes.Any(static node => node is not SyntaxToken))
                throw new InvalidDataException("Visible parity shader has malformed extension directive operands.");
            string[] operands = argumentNodes.Cast<SyntaxToken>().Select(static token => token.Text).ToArray();
            string requestedExtension = operands.FirstOrDefault() ?? string.Empty;
            int behaviorIndex;
            bool hasColon;
            if (requestedExtension.EndsWith(":", StringComparison.Ordinal))
            {
                requestedExtension = requestedExtension[..^1];
                behaviorIndex = 1;
                hasColon = true;
            }
            else if (operands.Length > 1 && operands[1] == ":")
            {
                behaviorIndex = 2;
                hasColon = true;
            }
            else
            {
                behaviorIndex = -1;
                hasColon = false;
            }

            if (!coreExtensions.Contains(requestedExtension) || !hasColon || behaviorIndex < 0 ||
                operands.Length != behaviorIndex + 1 ||
                operands[behaviorIndex] is not ("require" or "enable"))
                throw new InvalidDataException($"Visible parity shader requested unsupported extension '{requestedExtension}'.");
        }
        var editor = tree.CreateEditor()
            .Remove(uniforms)
            // Preserve each native uniform's initializer when present; use a constant zero for uninitialized inputs.
            .Replace(unboundUniforms, static node =>
            {
                var uniform = (GlUniformNode)node;
                string initializer = string.Concat(uniform.InitializerNodes.Select(static part => part.ToText()));
                if (uniform.InitializerNodes.Any())
                    return $"{uniform.TypeName} {uniform.Name} = {initializer};";

                string zero = uniform.TypeName switch
                {
                    "uint" => "0u",
                    "bool" => "false",
                    "double" => "double(0.0)",
                    _ when uniform.TypeName.StartsWith("u", StringComparison.Ordinal) => "0u",
                    _ when uniform.TypeName.StartsWith("i", StringComparison.Ordinal) => "0",
                    _ when uniform.TypeName.StartsWith("b", StringComparison.Ordinal) => "false",
                    _ when uniform.TypeName.StartsWith("d", StringComparison.Ordinal) => "double(0.0)",
                    _ => "0.0"
                };
                return $"{uniform.TypeName} {uniform.Name} = {uniform.TypeName}({zero});";
            });
        if (animationLayout is not null)
            editor.InsertBefore(animationLayout.Arguments.CloserNode, ", binding=1");
        editor.Remove(extensions)
            .Replace(version, "#version 450 core\n")
            .InsertAfter(version, $"\nlayout(std140, binding=0) uniform VgeParityInputs {{\n{members}\n}};\n")
            .InsertBefore(outputs, "layout(xfb_buffer=0, xfb_offset=0, xfb_stride=16) ")
            .Commit();

        // Preserve the engine animation block as its own declared binding instead of flattening or copying its body.
        return tree.ToText();
    }

    /// <summary>Requires the source AST to declare the four dynamic-light entries used by the fixture UBO.</summary>
    private static void ValidateDynamicLightDefine(SyntaxTree tree, bool arraysPresent)
    {
        if (!arraysPresent) return;
        var directives = tree.Select(Query.Syntax<GlDirectiveNode>()).Cast<GlDirectiveNode>().ToArray();
        var definitions = directives.Select((directive, index) => (directive, index, operands: DirectiveOperands(directive)))
            .Where(entry => entry.directive.Name == "define" && entry.operands.Length > 0 && entry.operands[0] == "DYNLIGHTS")
            .ToArray();
        var fixtureDefinition = definitions.Where(entry => entry.operands.SequenceEqual(["DYNLIGHTS", "4"])).ToArray();
        var fallbackDefinition = definitions.Where(entry => entry.operands.SequenceEqual(["DYNLIGHTS", "0"])).ToArray();
        if (definitions.Length != 2 || fixtureDefinition.Length != 1 || fallbackDefinition.Length != 1 ||
            fixtureDefinition[0].index >= fallbackDefinition[0].index)
            throw new InvalidDataException("Visible parity light arrays require the fixture DYNLIGHTS 4 definition before the native zero fallback.");

        int fallbackIndex = fallbackDefinition[0].index;
        if (fallbackIndex == 0 || fallbackIndex + 1 >= directives.Length ||
            directives[fallbackIndex - 1].Name != "ifndef" ||
            !DirectiveOperands(directives[fallbackIndex - 1]).SequenceEqual(["DYNLIGHTS"]) ||
            directives[fallbackIndex + 1].Name != "endif" || DirectiveOperands(directives[fallbackIndex + 1]).Length != 0)
            throw new InvalidDataException("Visible parity light arrays require the native DYNLIGHTS zero fallback to remain guarded.");

        static string[] DirectiveOperands(GlDirectiveNode directive)
        {
            SyntaxNode[] nodes = directive.Arguments.ToArray();
            return nodes.Any(static node => node is not SyntaxToken)
                ? []
                : nodes.Cast<SyntaxToken>().Select(static token => token.Text).ToArray();
        }
    }

    /// <summary>Uses the syntax tree to insert a native-visible feature define after the actual version directive.</summary>
    private static string AddDefines(string source, string defines)
    {
        var tree = SyntaxTree.Parse(source, GlslSchema.Instance);
        var version = tree.Select(Query.Syntax<GlDirectiveNode>().Named("version")).Cast<GlDirectiveNode>().Single();
        tree.CreateEditor().InsertAfter(version, "\n" + defines).Commit();
        return tree.ToText();
    }

    /// <summary>Creates the installed vertex ABI defaults, including terrain's integer colormap selector when requested.</summary>
    private static PointGeometry CreatePointGeometry(float[] position, int flags, int joint, bool terrain = false)
    {
        byte[] bytes = new byte[56];
        WriteFloat(bytes, 0, position[0]); WriteFloat(bytes, 4, position[1]); WriteFloat(bytes, 8, position[2]);
        WriteFloat(bytes, 12, 0); WriteFloat(bytes, 16, 0);
        WriteFloat(bytes, 20, 0); WriteFloat(bytes, 24, 0); WriteFloat(bytes, 28, 0); WriteFloat(bytes, 32, 1);
        WriteInt(bytes, 36, flags);
        if (terrain) WriteInt(bytes, 40, 0); else WriteFloat(bytes, 40, 0);
        WriteInt(bytes, 44, joint);
        var commands = RHICommandContext.Current;
        var layout = new RHIVertexLayout([
            new(0, 3, VertexComponentFormat.Float, VertexInterpretation.Floating, 0, 0, 56),
            new(1, 2, VertexComponentFormat.Float, VertexInterpretation.Floating, 0, 12, 56),
            new(2, 4, VertexComponentFormat.Float, VertexInterpretation.Floating, 0, 20, 56),
            new(3, 1, VertexComponentFormat.SignedInt, VertexInterpretation.Integer, 0, 36, 56),
            new(4, 1, terrain ? VertexComponentFormat.SignedInt : VertexComponentFormat.Float,
                terrain ? VertexInterpretation.Integer : VertexInterpretation.Floating, 0, 40, 56),
            new(5, 1, VertexComponentFormat.SignedInt, VertexInterpretation.Integer, 0, 44, 56),
            new(10, 2, VertexComponentFormat.UnsignedInt, VertexInterpretation.Integer, 0, 48, 56)
        ]);
        var buffer = commands.CreateVertexBuffer(new RHIBufferDescription
        {
            Role = BufferRole.Vertex, Mode = ResourceStorageMode.Mutable, Capacity = bytes.Length,
            Usage = BufferUsage.StaticDraw, Label = "Tests.VertexDeformationParity.Vertex"
        }, new(bytes)).RequireResource();
        return new(buffer, new(layout, [new(0, new(buffer, 0, bytes.Length), 56)], proceduralVertexCount: 1));
    }

    /// <summary>Creates one uniform buffer with a captured immutable initial byte range.</summary>
    private static GpuBufferObject CreateUniform(byte[] bytes) => RHICommandContext.Current.CreateBuffer(new RHIBufferDescription
    {
        Role = BufferRole.Uniform, Mode = ResourceStorageMode.Mutable, Capacity = bytes.Length,
        Usage = BufferUsage.DynamicDraw, Label = "Tests.VertexDeformationParity.Uniforms"
    }, new(bytes)).RequireResource();

    /// <summary>Creates captured resource bindings using the existing catalog's exact names and uniform-block slots.</summary>
    private static RHIResourceBindings Bindings(RHIShader shader, params (string Name, GpuBufferObject Buffer)[] buffers)
    {
        var bindings = new RHIBufferBinding[buffers.Length];
        for (int index = 0; index < buffers.Length; index++)
        {
            string name = buffers[index].Name;
            if (!shader.Description.Bindings.UniformBlocks.ContainsKey(name))
                throw new InvalidDataException($"The precompiled shadow stage does not declare uniform block '{name}'.");
            bindings[index] = new(GpuBindingEntry.Identity(ShaderBindingKind.UniformBlock, name),
                BufferRole.Uniform, buffers[index].Buffer);
        }
        return new(shader, buffers: bindings);
    }

    /// <summary>Packs the independently selected native visible inputs into fixed aligned std140 member slots.</summary>
    private static byte[] VisibleInputs(int dontWarpVertices, float[] modelTranslation, float[] origin)
    {
        byte[] data = new byte[1632];
        WriteTranslation(data, 0, modelTranslation);
        WriteIdentity(data, 64); WriteIdentity(data, 128); WriteIdentity(data, 192);
        WriteVec3(data, 256, origin);
        WriteVec3(data, 272, [3.25f, -1.5f, 2.75f]);
        WriteFloat(data, 288, 1.375f); WriteFloat(data, 304, .625f); WriteFloat(data, 320, 1.125f);
        WriteFloat(data, 336, .4f); WriteFloat(data, 352, .7f); WriteFloat(data, 368, .35f);
        WriteFloat(data, 384, .8f); WriteFloat(data, 400, .6f); WriteFloat(data, 416, .875f);
        WriteInt(data, 432, 2); WriteFloat(data, 448, .45f); WriteInt(data, 464, dontWarpVertices);
        return data;
    }

    /// <summary>Writes the legacy-native VgeShadowMaterial ABI at the producer's fixed 208-byte std140 offsets.</summary>
    private static byte[] MaterialInputs(float[] transform, float[] origin)
    {
        byte[] data = new byte[208];
        WriteTranslation(data, 0, transform); WriteVec3(data, 64, origin);
        WriteVec3(data, 80, [3.25f, -1.5f, 2.75f]);
        WriteFloat(data, 96, 1.375f); WriteFloat(data, 100, .625f); WriteFloat(data, 104, 1.125f);
        WriteFloat(data, 108, .875f); WriteFloat(data, 112, .4f); WriteFloat(data, 116, .7f);
        WriteFloat(data, 120, .35f); WriteFloat(data, 124, .8f); WriteFloat(data, 128, .6f);
        WriteInt(data, 132, 2); WriteFloat(data, 136, .45f); WriteIdentity(data, 144);
        return data;
    }

    /// <summary>Writes static-object flag values into their published two-integer block.</summary>
    private static byte[] ObjectFlags(int dontWarpVertices)
    {
        byte[] data = new byte[16]; WriteInt(data, 4, dontWarpVertices); return data;
    }

    /// <summary>Packs the exact animation palette extent used by both installed and expanded entity producers.</summary>
    private static byte[] Animation(float[] translation)
    {
        byte[] data = new byte[230 * 64]; WriteTranslation(data, 0, translation); return data;
    }

    /// <summary>Asserts visible/shadow transform-feedback positions remain equal within shader float tolerance.</summary>
    private static void AssertPositionsMatch(float[] visible, float[] shadow)
    {
        for (int component = 0; component < 4; component++)
            Assert.InRange(MathF.Abs(visible[component] - shadow[component]), 0f, 2e-5f);
    }

    /// <summary>Requires nonzero deformation after subtracting independently controlled placement and skin translation.</summary>
    private static void AssertMotion(float[] actual, float[] position, float[] placement, float[] skin)
    {
        float motion = 0;
        for (int component = 0; component < 3; component++)
            motion += MathF.Abs(actual[component] - position[component] - placement[component] - skin[component]);
        Assert.True(motion > 1e-4f, "The enabled shared deformation inputs produced no position change.");
    }

    /// <summary>Writes one column-major identity transform into aligned std140 storage.</summary>
    private static void WriteIdentity(byte[] data, int offset)
    {
        for (int index = 0; index < 4; index++) WriteFloat(data, offset + (index * 4 + index) * sizeof(float), 1f);
    }

    /// <summary>Writes one column-major translation matrix while keeping its std140 64-byte extent.</summary>
    private static void WriteTranslation(byte[] data, int offset, float[] translation)
    {
        WriteIdentity(data, offset);
        WriteFloat(data, offset + 48, translation[0]); WriteFloat(data, offset + 52, translation[1]);
        WriteFloat(data, offset + 56, translation[2]);
    }

    /// <summary>Writes a vec3 into its 16-byte-aligned std140 slot.</summary>
    private static void WriteVec3(byte[] data, int offset, float[] value)
    {
        WriteFloat(data, offset, value[0]); WriteFloat(data, offset + 4, value[1]); WriteFloat(data, offset + 8, value[2]);
    }

    /// <summary>Writes one float at an explicitly assigned UBO byte offset.</summary>
    private static void WriteFloat(byte[] data, int offset, float value) =>
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(offset, sizeof(float)), BitConverter.SingleToInt32Bits(value));

    /// <summary>Writes one signed integer at an explicitly assigned UBO byte offset.</summary>
    private static void WriteInt(byte[] data, int offset, int value) =>
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(offset, sizeof(int)), value);

    /// <summary>Provides every supported visible test field's native GLSL type and fixed aligned UBO offset.</summary>
    private static IReadOnlyDictionary<string, (string Type, int Offset)> VisibleUniformFields { get; } =
        new Dictionary<string, (string, int)>(StringComparer.Ordinal)
        {
            ["modelMatrix"] = ("mat4", 0), ["projectionMatrix"] = ("mat4", 64),
            ["modelViewMatrix"] = ("mat4", 128), ["viewMatrix"] = ("mat4", 192),
            ["origin"] = ("vec3", 256), ["playerpos"] = ("vec3", 272),
            ["timeCounter"] = ("float", 288), ["windWaveCounter"] = ("float", 304),
            ["windWaveCounterHighFreq"] = ("float", 320), ["windSpeed"] = ("float", 336),
            ["globalWarpIntensity"] = ("float", 352), ["glitchWaviness"] = ("float", 368),
            ["windWaveIntensity"] = ("float", 384), ["waterWaveIntensity"] = ("float", 400),
            ["waterWaveCounter"] = ("float", 416), ["perceptionEffectId"] = ("int", 432),
            ["perceptionEffectIntensity"] = ("float", 448), ["dontWarpVertices"] = ("int", 464)
        };

    /// <summary>Provides the only native visible arrays supported by the parity fixture and their fixed std140 spans.</summary>
    private static IReadOnlyDictionary<string, (string Type, int Offset, int Length, string Extent, string[] ExtentTokens)> VisibleUniformArrayFields { get; } =
        new Dictionary<string, (string, int, int, string, string[])>(StringComparer.Ordinal)
        {
            ["pointLights"] = ("vec3", 480, 4, "4", ["DYNLIGHTS"]),
            ["pointLightColors"] = ("vec3", 544, 4, "4", ["DYNLIGHTS"]),
            ["fogSpheres"] = ("float", 608, 24, "24", ["3", "*", "8"]),
            ["colorMapRects"] = ("vec4", 992, 40, "40", ["40"])
        };

    /// <summary>Maps the terrain relief sampled interface to its existing production texture units.</summary>
    private static IReadOnlyDictionary<string, int> TerrainReliefSamplerUnits { get; } =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["vge_displacementTex"] = global::VanillaGraphicsExpanded.PBR.Materials.TerrainReliefBindings.IndexUnit,
            ["vge_displacementRecords"] = global::VanillaGraphicsExpanded.PBR.Materials.TerrainReliefBindings.RecordUnit,
            ["vge_normalDepthTex"] = global::VanillaGraphicsExpanded.PBR.Materials.TerrainReliefBindings.HeightUnit
        };

    /// <summary>Owns a temporary RHI vertex stream and its typed geometry packet.</summary>
    private sealed class PointGeometry : IDisposable
    {
        private readonly GpuVbo buffer;
        internal RHIGeometryInputs Inputs { get; }
        /// <summary>Retains the RHI vertex buffer and its bounded one-point geometry packet.</summary>
        internal PointGeometry(GpuVbo buffer, RHIGeometryInputs inputs) { this.buffer = buffer; Inputs = inputs; }
        /// <summary>Retires the vertex buffer after all synchronous capture draws have completed.</summary>
        public void Dispose() => buffer.Dispose();
    }

    /// <summary>Owns the RHI-compiled visible shader and every uniform buffer referenced by its frozen bindings.</summary>
    private sealed class VisibleProgram : IDisposable
    {
        private readonly IDisposable[] resources;
        internal RHIShader Shader { get; }
        internal RHIResourceBindings Bindings { get; }
        /// <summary>Owns one RHI-compiled visible generation and every resource retained by its frozen bindings.</summary>
        internal VisibleProgram(RHIShader shader, RHIResourceBindings bindings, IEnumerable<IDisposable> resources)
        { Shader = shader; Bindings = bindings; this.resources = resources.ToArray(); }
        /// <summary>Releases captured resources before retiring their consuming shader generation.</summary>
        public void Dispose() { foreach (var resource in resources) resource.Dispose(); Shader.Dispose(); }
    }
    #endregion
}
