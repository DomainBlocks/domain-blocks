using System.Buffers;
using System.Text.Json;

namespace DomainBlocks.Serialization.SystemTextJson;

/// <summary>
/// Writes metadata as a flat JSON object straight from a span, without materializing a dictionary. The writer and
/// its buffer are cached per thread, so steady-state serialization allocates only the caller's copy of the output.
/// </summary>
internal static class MetadataJsonWriter
{
    [ThreadStatic] private static ArrayBufferWriter<byte>? t_buffer;
    [ThreadStatic] private static Utf8JsonWriter? t_writer;

    public static JsonWriterOptions GetWriterOptions(JsonSerializerOptions? options)
    {
        return options is null
            ? new JsonWriterOptions { SkipValidation = true }
            : new JsonWriterOptions
            {
                Encoder = options.Encoder,
                Indented = options.WriteIndented,
                IndentCharacter = options.IndentCharacter,
                IndentSize = options.IndentSize,
                NewLine = options.NewLine,
                SkipValidation = true
            };
    }

    /// <summary>
    /// Writes the metadata and returns the UTF-8 output. The returned span aliases a thread-cached buffer and is only
    /// valid until the next call on the same thread, so callers must copy it before returning.
    /// </summary>
    public static ReadOnlySpan<byte> Write(
        ReadOnlySpan<KeyValuePair<string, string>> metadata,
        in JsonWriterOptions writerOptions)
    {
        var buffer = t_buffer ??= new ArrayBufferWriter<byte>(256);
        buffer.ResetWrittenCount();

        var writer = t_writer;

        if (writer is null || !HasSameOptions(writer.Options, writerOptions))
        {
            writer?.Dispose();
            t_writer = writer = new Utf8JsonWriter(buffer, writerOptions);
        }
        else
        {
            writer.Reset(buffer);
        }

        writer.WriteStartObject();

        foreach (var (key, value) in metadata)
            writer.WriteString(key, value);

        writer.WriteEndObject();
        writer.Flush();

        return buffer.WrittenSpan;
    }

    private static bool HasSameOptions(in JsonWriterOptions current, in JsonWriterOptions requested)
    {
        return ReferenceEquals(current.Encoder, requested.Encoder) &&
               current.Indented == requested.Indented &&
               current.IndentCharacter == requested.IndentCharacter &&
               current.IndentSize == requested.IndentSize &&
               current.NewLine == requested.NewLine;
    }
}