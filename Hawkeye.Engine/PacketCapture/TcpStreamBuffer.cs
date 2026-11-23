using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace Hawkeye.Engine.PacketCapture
{

    public sealed class TcpStreamBuffer
    {
        private readonly ConcurrentDictionary<string, PacketAccumulator> _streamAccumulators = new();
        public event EventHandler<TcpPacketEventArgs>? PacketExtracted;
        private readonly ILogger<TcpStreamBuffer> logger;
        private readonly FilePacketWriter? filePacketWriter;

        public TcpStreamBuffer(ILogger<TcpStreamBuffer> logger, FilePacketWriter? filePacketWriter = null)
        {
            this.logger = logger;
            this.filePacketWriter = filePacketWriter;
        }

        public void AddData(string streamKey, byte[] payload, long arrivedAt)
        {
            var accumulator = _streamAccumulators.GetOrAdd(streamKey, _ => new PacketAccumulator(logger, () => Interlocked.Increment(ref keepAlives)));
            filePacketWriter?.LogPacket(streamKey, payload);
            accumulator.AppendAndProcess(payload, (packet) =>
            {
                PacketExtracted?.Invoke(this, new TcpPacketEventArgs
                {
                    StreamKey = streamKey,
                    Payload = packet,
                    ReceivedAt = arrivedAt
                });
            });
        }

        public void Clear()
        {
            foreach (var accumulator in _streamAccumulators.Values) accumulator.Clear();
            _streamAccumulators.Clear();
            filePacketWriter?.FinalizePacketLogging();
        }

        public void ClearStream(string streamKey)
        {
            if (_streamAccumulators.TryRemove(streamKey, out var accumulator)) accumulator.Clear();
        }

        public int ActiveStreamsCount => _streamAccumulators.Count;

        /// <summary>
        /// Game keep-alives read so far, all streams. The capture uses it to prove a locked port really is the game:
        /// a web connection (port 443) can carry the 3 detection bytes by chance, but never a whole keep-alive in step.
        /// </summary>
        public long KeepAlives => Interlocked.Read(ref keepAlives);
        private long keepAlives;
    }
}