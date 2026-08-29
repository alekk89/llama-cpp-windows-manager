namespace LocalLlmConsole.Services;

public sealed record GgufMetadataInspectionResult(
    bool Success,
    IReadOnlyDictionary<string, object?> Values,
    string Error)
{
    public static GgufMetadataInspectionResult Failed(string error)
        => new(false, new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase), error);
}

public static class GgufMetadataReader
{
    private const uint MinSupportedVersion = 1;
    private const uint MaxSupportedVersion = 3;
    private const ulong MaxMetadataEntries = 100_000UL;
    private const ulong MaxMetadataBytes = 1024UL * 1024UL * 1024UL;
    private const ulong MaxArrayElements = 10_000_000UL;
    private const ulong MaxStringBytes = 1024UL * 1024UL;
    private const int MaxArrayNesting = 8;

    public static GgufMetadataInspectionResult Inspect(string path)
    {
        var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: false);
            EnsureHeader(reader);
            _ = reader.ReadUInt64();
            var metadataCount = reader.ReadUInt64();
            if (metadataCount > MaxMetadataEntries)
                throw new InvalidDataException("GGUF metadata contains too many entries.");

            var metadataStart = stream.Position;
            for (ulong index = 0; index < metadataCount; index++)
            {
                EnsureMetadataBudget(stream, metadataStart);
                var key = ReadString(reader);
                var type = ReadType(reader);
                values[key] = ReadValue(reader, type);
            }
            EnsureMetadataBudget(stream, metadataStart);
            return new GgufMetadataInspectionResult(true, values, "");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or OverflowException)
        {
            return GgufMetadataInspectionResult.Failed(ex.Message);
        }
    }

    public static IReadOnlyDictionary<string, object?> TryRead(string path)
    {
        var inspection = Inspect(path);
        return inspection.Success
            ? inspection.Values
            : new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
    }

    public static long? TryReadParameterCount(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: false);
            EnsureHeader(reader);
            var tensorCount = reader.ReadUInt64();
            var metadataCount = reader.ReadUInt64();
            if (tensorCount is 0 or > 1_000_000 || metadataCount > MaxMetadataEntries) return null;

            var metadataStart = stream.Position;
            for (ulong index = 0; index < metadataCount; index++)
            {
                EnsureMetadataBudget(stream, metadataStart);
                SkipString(reader);
                SkipValue(reader, ReadType(reader), 0);
            }
            EnsureMetadataBudget(stream, metadataStart);

            ulong total = 0;
            for (ulong index = 0; index < tensorCount; index++)
            {
                SkipString(reader);
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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or OverflowException)
        {
            return null;
        }
    }

    private static void EnsureHeader(BinaryReader reader)
    {
        if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "GGUF")
            throw new InvalidDataException("The file does not have a GGUF header.");
        var version = reader.ReadUInt32();
        if (version is < MinSupportedVersion or > MaxSupportedVersion)
            throw new InvalidDataException($"GGUF version {version} is not supported.");
    }

    private static void EnsureMetadataBudget(Stream stream, long metadataStart)
    {
        var consumed = stream.Position - metadataStart;
        if (consumed < 0 || (ulong)consumed > MaxMetadataBytes)
            throw new InvalidDataException("GGUF metadata exceeds the supported inspection limit.");
    }

    private static GgufValueType ReadType(BinaryReader reader)
    {
        var type = (GgufValueType)reader.ReadUInt32();
        if (type is < GgufValueType.UInt8 or > GgufValueType.Float64)
            throw new InvalidDataException($"Unsupported GGUF metadata value type: {(uint)type}.");
        return type;
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
        GgufValueType.Array => ReadArraySummary(reader, 0),
        GgufValueType.UInt64 => reader.ReadUInt64(),
        GgufValueType.Int64 => reader.ReadInt64(),
        GgufValueType.Float64 => reader.ReadDouble(),
        _ => throw new InvalidDataException($"Unsupported GGUF metadata value type: {(uint)type}.")
    };

    private static string ReadArraySummary(BinaryReader reader, int depth)
    {
        var elementType = ReadType(reader);
        var length = reader.ReadUInt64();
        SkipArrayElements(reader, elementType, length, depth);
        return FormattableString.Invariant($"{length:N0} {elementType} values");
    }

    private static void SkipValue(BinaryReader reader, GgufValueType type, int depth)
    {
        if (type == GgufValueType.Array)
        {
            var elementType = ReadType(reader);
            var length = reader.ReadUInt64();
            SkipArrayElements(reader, elementType, length, depth);
            return;
        }
        if (type == GgufValueType.String)
        {
            SkipString(reader);
            return;
        }

        var bytes = FixedSize(type);
        if (bytes == 0)
            throw new InvalidDataException($"Unsupported GGUF metadata value type: {(uint)type}.");
        SkipBytes(reader, (ulong)bytes);
    }

    private static void SkipArrayElements(BinaryReader reader, GgufValueType elementType, ulong length, int depth)
    {
        if (depth >= MaxArrayNesting)
            throw new InvalidDataException("GGUF metadata arrays are nested too deeply.");
        if (length > MaxArrayElements)
            throw new InvalidDataException("GGUF metadata array contains too many elements.");

        var fixedSize = FixedSize(elementType);
        if (fixedSize > 0)
        {
            SkipBytes(reader, checked(length * (ulong)fixedSize));
            return;
        }
        if (elementType == GgufValueType.String)
        {
            for (ulong index = 0; index < length; index++)
                SkipString(reader);
            return;
        }
        if (elementType == GgufValueType.Array)
        {
            for (ulong index = 0; index < length; index++)
            {
                var nestedElementType = ReadType(reader);
                var nestedLength = reader.ReadUInt64();
                SkipArrayElements(reader, nestedElementType, nestedLength, depth + 1);
            }
            return;
        }

        throw new InvalidDataException($"Unsupported GGUF metadata array element type: {(uint)elementType}.");
    }

    private static string ReadString(BinaryReader reader)
    {
        var length = ReadStringLength(reader);
        return Encoding.UTF8.GetString(reader.ReadBytes(checked((int)length)));
    }

    private static void SkipString(BinaryReader reader)
        => SkipBytes(reader, ReadStringLength(reader));

    private static ulong ReadStringLength(BinaryReader reader)
    {
        var length = reader.ReadUInt64();
        if (length > MaxStringBytes)
            throw new InvalidDataException("GGUF string is too large.");
        if (length > (ulong)Math.Max(0, reader.BaseStream.Length - reader.BaseStream.Position))
            throw new EndOfStreamException("GGUF string extends past the end of the file.");
        return length;
    }

    private static void SkipBytes(BinaryReader reader, ulong bytes)
    {
        if (bytes > MaxMetadataBytes)
            throw new InvalidDataException("GGUF metadata value is too large.");
        if (bytes > (ulong)Math.Max(0, reader.BaseStream.Length - reader.BaseStream.Position))
            throw new EndOfStreamException("GGUF metadata value extends past the end of the file.");
        reader.BaseStream.Seek(checked((long)bytes), SeekOrigin.Current);
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
