namespace LocalLlmConsole.Services;

internal static class PortableExecutableLayout
{
    private const int DosPeHeaderOffset = 0x3c;
    private const int PeHeaderSize = 24;
    private const int CertificateDirectoryIndex = 4;

    public static long ContentEndBeforeCertificate(Stream executable)
    {
        if (!executable.CanSeek || executable.Length < 64) return executable.Length;

        Span<byte> dosHeader = stackalloc byte[64];
        executable.Position = 0;
        executable.ReadExactly(dosHeader);
        if (dosHeader[0] != (byte)'M' || dosHeader[1] != (byte)'Z') return executable.Length;

        var peOffset = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(
            dosHeader.Slice(DosPeHeaderOffset, sizeof(int)));
        if (peOffset < 64 || peOffset > executable.Length - PeHeaderSize)
            throw new InvalidDataException("Packaged executable has an invalid PE header offset.");

        Span<byte> peHeader = stackalloc byte[PeHeaderSize];
        executable.Position = peOffset;
        executable.ReadExactly(peHeader);
        if (!peHeader[..4].SequenceEqual("PE\0\0"u8))
            throw new InvalidDataException("Packaged executable has an invalid PE signature.");

        var optionalHeaderSize = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(peHeader.Slice(20, 2));
        var optionalHeaderOffset = peOffset + PeHeaderSize;
        if (optionalHeaderSize < 2 || optionalHeaderOffset > executable.Length - optionalHeaderSize)
            throw new InvalidDataException("Packaged executable has an invalid optional header.");

        Span<byte> magicBytes = stackalloc byte[2];
        executable.Position = optionalHeaderOffset;
        executable.ReadExactly(magicBytes);
        var magic = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(magicBytes);
        var dataDirectoryOffset = magic switch
        {
            0x10b => 96,
            0x20b => 112,
            _ => throw new InvalidDataException("Packaged executable has an unsupported optional header.")
        };
        var certificateEntryOffset = dataDirectoryOffset + (CertificateDirectoryIndex * 8);
        if (certificateEntryOffset + 8 > optionalHeaderSize) return executable.Length;

        Span<byte> certificateEntry = stackalloc byte[8];
        executable.Position = optionalHeaderOffset + certificateEntryOffset;
        executable.ReadExactly(certificateEntry);
        var certificateOffset = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(certificateEntry[..4]);
        var certificateSize = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(certificateEntry[4..]);
        if (certificateOffset == 0 && certificateSize == 0) return executable.Length;
        if (certificateOffset == 0 || certificateSize == 0 || certificateOffset > executable.Length - certificateSize)
            throw new InvalidDataException("Packaged executable has an invalid certificate table.");

        return certificateOffset;
    }
}
