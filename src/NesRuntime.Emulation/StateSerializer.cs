using System.Text;

namespace NesRuntime.Emulation;

/// <summary>
/// A one-pass, bidirectional serializer for save states. Each component's <c>SerializeState</c> lists
/// its fields once; the same body both <em>writes</em> (save) and <em>reads</em> (load) depending on
/// the direction, so the two can never drift out of sync. Arrays are filled in place (never
/// reallocated) because other components hold references to them (e.g. mapper PRG-RAM, PPU VRAM).
/// </summary>
public sealed class StateSerializer
{
    private readonly BinaryWriter? _writer;
    private readonly BinaryReader? _reader;

    private StateSerializer(BinaryWriter w) => _writer = w;
    private StateSerializer(BinaryReader r) => _reader = r;

    /// <summary>True while saving (writing); false while loading (reading).</summary>
    public bool IsWriting => _writer is not null;

    /// <summary>Runs <paramref name="body"/> in write mode and returns the serialized bytes.</summary>
    public static byte[] Save(Action<StateSerializer> body)
    {
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
            body(new StateSerializer(w));
        return ms.ToArray();
    }

    /// <summary>Runs <paramref name="body"/> in read mode over <paramref name="data"/>.</summary>
    public static void Load(ReadOnlySpan<byte> data, Action<StateSerializer> body)
    {
        using var ms = new MemoryStream(data.ToArray(), writable: false);
        using var r = new BinaryReader(ms, Encoding.UTF8);
        body(new StateSerializer(r));
    }

    public void Transfer(ref byte v) { if (_writer is not null) _writer.Write(v); else v = _reader!.ReadByte(); }
    public void Transfer(ref bool v) { if (_writer is not null) _writer.Write(v); else v = _reader!.ReadBoolean(); }
    public void Transfer(ref int v) { if (_writer is not null) _writer.Write(v); else v = _reader!.ReadInt32(); }
    public void Transfer(ref long v) { if (_writer is not null) _writer.Write(v); else v = _reader!.ReadInt64(); }
    public void Transfer(ref ushort v) { if (_writer is not null) _writer.Write(v); else v = _reader!.ReadUInt16(); }
    public void Transfer(ref float v) { if (_writer is not null) _writer.Write(v); else v = _reader!.ReadSingle(); }
    public void Transfer(ref double v) { if (_writer is not null) _writer.Write(v); else v = _reader!.ReadDouble(); }

    public void Transfer(ref Mirroring v) { int i = (int)v; Transfer(ref i); v = (Mirroring)i; }

    /// <summary>Transfers a fixed-length byte array in place.</summary>
    public void TransferBytes(byte[] a)
    {
        if (_writer is not null) { _writer.Write(a, 0, a.Length); return; }
        int off = 0;
        while (off < a.Length) off += _reader!.Read(a, off, a.Length - off);
    }

    /// <summary>Transfers a fixed-length int array in place.</summary>
    public void Transfer(int[] a)
    {
        for (int i = 0; i < a.Length; i++) { int v = a[i]; Transfer(ref v); a[i] = v; }
    }
}
