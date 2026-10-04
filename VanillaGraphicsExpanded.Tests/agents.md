# Writing VanillaGraphicsExpanded Tests

This document provides guidelines and instructions for writing tests for the VanillaGraphicsExpanded project. It covers the structure, conventions, and best practices for creating reliable and maintainable test cases.

## Shaders

Shader tests MUST adhere to the following guidelines:

- **VGE-owned shaders**: Tests must use precompiled SPIR-V rather than compiling raw GLSL or HLSL at runtime.
- **Vanilla engine shaders**: Base-game shaders, including VGE-patched variants, may use their GLSL compilation path. Reuse the existing installed-source and driver compilation fixtures; do not add SPIR-V export machinery for them.
- **DRY (Don't Repeat Yourself)**: Tests must reuse the existing GPU abstractions and avoid duplicating setup or teardown code.
- **Abstraction**: Tests should aim to abstract common setup and teardown logic to reduce duplication and improve maintainability.
- **Clarity**: Tests should be written clearly and concisely to ensure they are easy to understand and maintain.
- **Isolation**: Each shader test should be self-contained and not depend on the state of other tests.
- **Validation**: Tests must verify the correctness of shader outputs against expected results.
- **Documentation**: Each test should include a description of its purpose and code comments which explain the test logic and any important details.

## Use the existing GPU abstractions

Tests should leverage the existing GPU abstractions provided by the VanillaGraphicsExpanded project. This ensures consistency, reduces boilerplate code, and makes tests easier to maintain. Avoid directly interacting with low-level GPU APIs unless absolutely necessary.

### List of GPU Abstractions

Below is a partial list of the GPU abstractions available in the VanillaGraphicsExpanded project, consult the project documentation for a complete list and detailed usage instructions.

- **GlPipelineDesc**: Represents a pipeline description abstraction for configuring GPU pipeline state.
- **GpuStateCache**: Represents a GPU state cache abstraction for managing and optimizing GPU state changes.
- **GpuVao**: Represents a Vertex Array Object (VAO) abstraction for managing vertex attribute state.
- **GpuVbo**: Represents a Vertex Buffer Object (VBO) abstraction for managing vertex data.
- **GpuEbo**: Represents an Element Buffer Object (EBO) abstraction for managing index data.
- **GpuBuffer**: Represents a GPU buffer abstraction for storing vertex or index data.
- **GpuProgram**: Represents a compiled shader program abstraction.
- **GpuTexture**: Represents a GPU texture abstraction for managing texture data.
- **GpuFramebuffer**: Represents a GPU framebuffer abstraction for managing render targets.
- **GpuRenderbuffer**: Represents a GPU renderbuffer abstraction for managing renderbuffer storage.
