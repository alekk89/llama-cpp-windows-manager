
namespace LocalLlmConsole.Services;

public static class GgufMetadataReader
{
    private const uint MinSupportedVersion = 1;
    private const uint MaxSupportedVersion = 3;
    private const ulong MaxMetadataBytes = 64UL * 1024UL * 1024UL;
    private const ulong MaxArrayElements = 100_000UL;
    private const ulong MaxSummaryMetadataBytes = 1024UL * 1024UL * 1024UL;
    private const ulong MaxSummaryArrayElements = 10_000_000UL;

    public static IReadOnlyDictionary<string, object?> TryRead(string path, int maxKeys = 512)
    {
        var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: false);
            if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "GGUF") return values;

            var version = reader.ReadUInt32();
            if (version is < MinSupportedVersion or > MaxSupportedVersion) return values;
            _ = reader.ReadUInt64();
            var metadataCount = reader.ReadUInt64();
            var metadataStart = stream.Position;
            var count = (int)Math.Min(metadataCount, (ulong)Math.Max(0, maxKeys));
            for (var i = 0; i < count; i++)
            {
                if ((ulong)(stream.Position - metadataStart) > MaxMetadataBytes) break;
                var key = ReadString(reader);
                var type = (GgufValueType)reader.ReadUInt32();
                values[key] = ReadValue(reader, type);
            }
        }
        catch
        {
            return values;
        }

        return values;
    }

    public static long? TryReadParameterCount(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: false);
            if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "GGUF") return null;

            var version = reader.ReadUInt32();
            if (version is < MinSupportedVersion or > MaxSupportedVersion) return null;
            var tensorCount = reader.ReadUInt64();
            var metadataCount = reader.ReadUInt64();
            if (tensorCount is 0 or > 1_000_000 || metadataCount > 100_000) return null;

            var metadataStart = stream.Position;
            for (ulong index = 0; index < metadataCount; index++)
            {
                if ((ulong)(stream.Position - metadataStart) > MaxSummaryMetadataBytes) return null;
                _ = ReadString(reader);
                if (!SkipValue(reader, (GgufValueType)reader.ReadUInt32())) return null;
            }

            ulong total = 0;
            for (ulong index = 0; index < tensorCount; index++)
            {
                _ = ReadString(reader);
                var dimensions = reader.ReadUInt32();
                if (dimensions is 0 or > 8) return null;
                ulong elements = 1;
                for (var dimension = 0; dimension < dimensions; dimension++)
                    elements = checked(elements * reader.ReadUInt64());
                _ = reader.ReadUInt32();
                _ = reader.ReadUInt64();
                total = checked(total + elements);
            }

            return total is > 0 and <= long.MaxValue ? (long)total : null;
        }
        catch
        {
            return null;
        }
    }

    private static bool SkipValue(BinaryReader reader, GgufValueType type)
    {
        if (type != GgufValueType.Array)
        {
            _ = ReadValue(reader, type);
            return true;
        }

        var elementType = (GgufValueType)reader.ReadUInt32();
        var length = reader.ReadUInt64();
        if (length > MaxSummaryArrayElements) return false;
        var fixedSize = FixedSize(elementType);
        if (fixedSize > 0)
        {
            var bytes = checked(length * (ulong)fixedSize);
            return SkipBytes(reader, bytes);
        }
        if (elementType != GgufValueType.String) return false;

        ulong totalBytes = 0;
        for (ulong index = 0; index < length; index++)
        {
            var stringLength = reader.ReadUInt64();
            if (stringLength > 1024 * 1024) return false;
            totalBytes = checked(totalBytes + stringLength);
            if (totalBytes > MaxSummaryMetadataBytes || !SkipBytes(reader, stringLength)) return false;
        }
        return true;
    }

    private static bool SkipBytes(BinaryReader reader, ulong bytes)
    {
        if (bytes > MaxSummaryMetadataBytes
            || bytes > (ulong)Math.Max(0, reader.BaseStream.Length - reader.BaseStream.Position))
            return false;
        reader.BaseStream.Seek(checked((long)bytes), SeekOrigin.Current);
        return true;
    }

    private static object? ReadValue(BinaryReader reader, GgufValueType type) => type switch
    {
        GgufValueType.UInt8 => reader.ReadByte(),
        GgufValueType.Int8 => reader.ReadSByte(),
        GgufValueType.UInt16 => reader.ReadUInt16(),
        GgufValueType.Int16 => reader.ReadInt16(),
        GgufValueType.UInt32 => reader.ReadUInt32(),
        GgufValueType.Int32 => reader.ReadInt32(),
        GgufValueType.Float32 => reader.ReadSingle(),
        GgufValueType.Bool => reader.ReadByte() != 0,
        GgufValueType.String => ReadString(reader),
        GgufValueType.Array => ReadArraySummary(reader),
        GgufValueType.UInt64 => reader.ReadUInt64(),
        GgufValueType.Int64 => reader.ReadInt64(),
        GgufValueType.Float64 => reader.ReadDouble(),
        _ => null
    };

    private static string ReadString(BinaryReader reader)
    {
        var length = reader.ReadUInt64();
        if (length > 1024 * 1024) throw new InvalidDataException("GGUF string is too large.");
        if (length > (ulong)Math.Max(0, reader.BaseStream.Length - reader.BaseStream.Position))
            throw new EndOfStreamException("GGUF string extends past the end of the file.");
        return Encoding.UTF8.GetString(reader.ReadBytes((int)length));
    }

    private static string ReadArraySummary(BinaryReader reader)
    {
        var elementType = (GgufValueType)reader.ReadUInt32();
        var length = reader.ReadUInt64();
        if (length > MaxArrayElements) throw new InvalidDataException("GGUF metadata array is too large.");
        var fixedSize = FixedSize(elementType);
        if (fixedSize > 0)
        {
            var bytes = checked(length * (ulong)fixedSize);
            if (bytes > MaxMetadataBytes) throw new InvalidDataException("GGUF metadata array is too large.");
            if (bytes > (ulong)Math.Max(0, reader.BaseStream.Length - reader.BaseStream.Position))
                throw new EndOfStreamException("GGUF array extends past the end of the file.");
            reader.BaseStream.Seek(checked((long)bytes), SeekOrigin.Current);
        }
        else if (elementType == GgufValueType.String)
        {
            for (ulong i = 0; i < length; i++)
            {
                var stringLength = reader.ReadUInt64();
                if (stringLength > 1024 * 1024) throw new InvalidDataException("GGUF array string is too large.");
                if (stringLength > (ulong)Math.Max(0, reader.BaseStream.Length - reader.BaseStream.Position))
                    throw new EndOfStreamException("GGUF array string extends past the end of the file.");
                reader.BaseStream.Seek(checked((long)stringLength), SeekOrigin.Current);
            }
        }
        else
        {
            for (ulong i = 0; i < length; i++)
                _ = ReadValue(reader, elementType);
        }
        return $"{length:N0} {elementType} values";
    }

    private static int FixedSize(GgufValueType type) => type switch
    {
        GgufValueType.UInt8 or GgufValueType.Int8 or GgufValueType.Bool => 1,
        GgufValueType.UInt16 or GgufValueType.Int16 => 2,
        GgufValueType.UInt32 or GgufValueType.Int32 or GgufValueType.Float32 => 4,
        GgufValueType.UInt64 or GgufValueType.Int64 or GgufValueType.Float64 => 8,
        _ => 0
    };

    private enum GgufValueType : uint
    {
        UInt8 = 0,
        Int8 = 1,
        UInt16 = 2,
        Int16 = 3,
        UInt32 = 4,
        Int32 = 5,
        Float32 = 6,
        Bool = 7,
        String = 8,
        Array = 9,
        UInt64 = 10,
        Int64 = 11,
        Float64 = 12
    }
}
