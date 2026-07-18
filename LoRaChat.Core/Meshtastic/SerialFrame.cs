namespace LoRaChat.Core.Meshtastic;

/// <summary>
/// Meshtastic serial stream framing. Each protobuf packet on the wire is prefixed with
/// <c>0x94 0xC3</c> and a 16-bit big-endian length: <c>[0x94][0xC3][len_hi][len_lo][payload…]</c>.
/// The radio also emits unframed debug text, which the deframer discards (surfaced via
/// <see cref="DebugText"/>). This is the piece the plan calls out as living in Core.
/// </summary>
public sealed class SerialDeframer
{
    private const byte Start1 = 0x94;
    private const byte Start2 = 0xC3;
    private const int MaxPayload = 512;

    private readonly List<byte> _buffer = new();

    /// <summary>Raised with any run of non-framed bytes (the radio's debug log output).</summary>
    public event EventHandler<string>? DebugText;

    /// <summary>Feeds received bytes and returns any complete FromRadio protobuf payloads now available.</summary>
    public IReadOnlyList<byte[]> Push(ReadOnlySpan<byte> data)
    {
        foreach (byte b in data) _buffer.Add(b);
        var frames = new List<byte[]>();

        while (true)
        {
            // Find the START1 byte; anything before it is debug text.
            int start = _buffer.IndexOf(Start1);
            if (start < 0)
            {
                FlushDebug(_buffer.Count);
                break;
            }
            if (start > 0) FlushDebug(start);

            if (_buffer.Count < 2) break; // need START2
            if (_buffer[1] != Start2)
            {
                // Lone 0x94 that isn't a frame start — treat it as debug and move on.
                FlushDebug(1);
                continue;
            }
            if (_buffer.Count < 4) break; // need the length bytes

            int len = (_buffer[2] << 8) | _buffer[3];
            if (len > MaxPayload)
            {
                // Corrupt length — resync by dropping the START1 and scanning again.
                FlushDebug(1);
                continue;
            }
            if (_buffer.Count < 4 + len) break; // wait for the full payload

            byte[] payload = _buffer.GetRange(4, len).ToArray();
            _buffer.RemoveRange(0, 4 + len);
            frames.Add(payload);
        }

        return frames;
    }

    private void FlushDebug(int count)
    {
        if (count <= 0) return;
        if (DebugText != null)
        {
            string text = System.Text.Encoding.UTF8.GetString(_buffer.GetRange(0, count).ToArray());
            if (!string.IsNullOrWhiteSpace(text)) DebugText.Invoke(this, text.TrimEnd('\r', '\n'));
        }
        _buffer.RemoveRange(0, count);
    }

    /// <summary>Wraps a protobuf payload in the serial frame header.</summary>
    public static byte[] Frame(byte[] payload)
    {
        int len = payload.Length;
        var framed = new byte[4 + len];
        framed[0] = Start1;
        framed[1] = Start2;
        framed[2] = (byte)((len >> 8) & 0xFF);
        framed[3] = (byte)(len & 0xFF);
        Buffer.BlockCopy(payload, 0, framed, 4, len);
        return framed;
    }
}
