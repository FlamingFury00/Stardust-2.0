using System.Net.Sockets;
using Google.FlatBuffers;
using RLBot.Flat;

namespace RLBot.Util;

/**
 * Reads messages to RLBot server according to spec:
 * https://wiki.rlbot.org/framework/sockets-specification/
 */
class SpecStreamReader
{
    private BufferedStream _bufferedStream;
    private byte[] _ushortReader = new byte[2];
    private byte[] _payloadReader = new byte[ushort.MaxValue];

    public SpecStreamReader(NetworkStream stream)
    {
        _bufferedStream = new BufferedStream(stream, 2 + ushort.MaxValue);
    }

    /// <summary>Attempt to read an incoming message</summary>
    /// <exception cref="SocketException">Thrown if there are no incoming messages.</exception>
    public CorePacket ReadOne()
    {
        ushort payloadSize;
        _bufferedStream.ReadExactly(_ushortReader);
        payloadSize = ReadBigEndian(_ushortReader);
        _bufferedStream.ReadExactly(_payloadReader, 0, payloadSize);
        
        ByteBuffer byteBuffer = new(_payloadReader, 0);
        return CorePacket.GetRootAsCorePacket(byteBuffer);
    }

    private static ushort ReadBigEndian(Span<byte> bytes)
    {
        return (ushort)((bytes[0] << 8) | bytes[1]);
    }
}
