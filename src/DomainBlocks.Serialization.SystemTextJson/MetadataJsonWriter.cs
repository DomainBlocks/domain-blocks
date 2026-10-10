using System.Buffers;
using System.Text.Json;

namespace DomainBlocks.Serialization.SystemTextJson;

/// <summary>
/// Writes metadata as a flat JSON object. Each thread reuses one writer and one buffer rather than allocating them for
/// each call.
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
    /// Writes metadata as UTF-8 JSON. The result is valid only until the next call on the same thread.
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