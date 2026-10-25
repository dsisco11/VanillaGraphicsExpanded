using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns exact Ext Uniform1 execution and affected metadata completion.</summary>
internal sealed unsafe partial class StateCache
{
    #region Internal API
    /// <summary>Executes the exact external overload and completes affected metadata publication.</summary>
    internal void ExecuteExtUniform1(int @location, int @count, ref int @value)
    {
        var observation = StateCache.BeginNativeProgramMetadata(0, false, true, unchecked((int)@location), unchecked((int)@count), false, false);
        bool returned = false;
        try
        {
            GL.Ext.Uniform1(@location, @count, ref @value);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes affected metadata publication.</summary>
    internal void ExecuteExtUniform1(int @location, int @count, int[] @value)
    {
        var observation = StateCache.BeginNativeProgramMetadata(0, false, true, unchecked((int)@location), unchecked((int)@count), false, false);
        bool returned = false;
        try
        {
            GL.Ext.Uniform1(@location, @count, @value);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes affected metadata publication.</summary>
    internal void ExecuteExtUniform1(int @location, int @count, int* @value)
    {
        var observation = StateCache.BeginNativeProgramMetadata(0, false, true, unchecked((int)@location), unchecked((int)@count), false, false);
        bool returned = false;
        try
        {
            GL.Ext.Uniform1(@location, @count, @value);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes affected metadata publication.</summary>
    internal void ExecuteExtUniform1(int @location, int @v0)
    {
        var observation = StateCache.BeginNativeProgramMetadata(0, false, true, unchecked((int)@location), 1, false, false);
        bool returned = false;
        try
        {
            GL.Ext.Uniform1(@location, @v0);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }
    #endregion
}
