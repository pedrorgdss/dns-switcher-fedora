using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace DnsSwitcher.Core.Services;

internal static class DnsPacket
{
    private static ushort U16(ReadOnlySpan<byte> packet, int offset) =>
        BinaryPrimitives.ReadUInt16BigEndian(packet[offset..]);

    public static byte[] Query(string domain)
    {
        using var query = new MemoryStream();
        var header = new byte[12];
        RandomNumberGenerator.Fill(header.AsSpan(0, 2));
        header[2] = 1; // Recursion desired.
        header[5] = 1; // One question.
        query.Write(header);
        foreach (var label in domain.Split('.'))
        {
            var bytes = Encoding.ASCII.GetBytes(label);
            query.WriteByte((byte)bytes.Length);
            query.Write(bytes);
        }
        query.Write(new byte[] { 0, 0, 1, 0, 1 }); // root, A, IN
        return query.ToArray();
    }

    public static bool HasAnswer(ReadOnlySpan<byte> packet, ReadOnlySpan<byte> query, string domain)
    {
        if (packet.Length < 12 || U16(packet, 0) != U16(query, 0) ||
            (U16(packet, 2) & 0xFA0F) != 0x8000 || U16(packet, 4) != 1) return false;
        int cursor = 12;
        if (!ReadName(packet, ref cursor, out var name) ||
            !domain.Equals(name, StringComparison.OrdinalIgnoreCase) || cursor + 4 > packet.Length ||
            U16(packet, cursor) != 1 || U16(packet, cursor + 2) != 1) return false;
        cursor += 4;
        // CNAMEs may precede the A record. A truncated or negative reply is a failed sample.
        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var addresses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int answers = U16(packet, 6);
        for (int i = 0; i < answers; i++)
        {
            if (!ReadName(packet, ref cursor, out var owner) || cursor + 10 > packet.Length) return false;
            int type = U16(packet, cursor), cls = U16(packet, cursor + 2), size = U16(packet, cursor + 8);
            cursor += 10;
            int end = cursor + size;
            if (end > packet.Length) return false;
            if (cls == 1 && type == 1 && size == 4) addresses.Add(owner);
            if (cls == 1 && type == 5)
            {
                if (!ReadName(packet, ref cursor, out var alias) || cursor != end) return false;
                aliases[owner] = alias;
            }
            cursor = end;
        }
        string target = domain;
        for (int i = 0; i <= aliases.Count; i++)
        {
            if (addresses.Contains(target)) return true;
            if (!aliases.TryGetValue(target, out var next)) break;
            target = next;
        }
        return false;
    }

    private static bool ReadName(ReadOnlySpan<byte> packet, ref int cursor, out string name)
    {
        name = "";
        int pos = cursor, resume = -1, hops = 0;
        var result = new StringBuilder();
        while (pos < packet.Length && hops++ < 160)
        {
            int length = packet[pos++];
            if (length == 0)
            {
                cursor = resume < 0 ? pos : resume;
                name = result.ToString();
                return true;
            }
            if ((length & 0xC0) == 0xC0)
            {
                if (pos >= packet.Length) return false;
                int offset = ((length & 63) << 8) | packet[pos++];
                if (offset >= pos - 2) return false;
                if (resume == -1) resume = pos;
                pos = offset;
                continue;
            }
            if (length > 63 || pos + length > packet.Length) return false;
            if (result.Length > 0) result.Append('.');
            result.Append(Encoding.ASCII.GetString(packet.Slice(pos, length)));
            if (result.Length > 253) return false;
            pos += length;
        }
        return false;
    }
}
