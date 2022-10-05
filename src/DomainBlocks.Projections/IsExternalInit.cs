using System.ComponentModel;

// ReSharper disable once CheckNamespace
// This is added to enable init only properties without targeting .NET 5.0
namespace System.Runtime.CompilerServices;

/// <summary>
/// Reserved to be used by the compiler for tracking metadata.
/// This class should not be used by developers in source code.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class IsExternalInit
{
}