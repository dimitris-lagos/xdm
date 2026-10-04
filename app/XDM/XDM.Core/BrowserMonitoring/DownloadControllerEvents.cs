using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace XDM.Core.BrowserMonitoring
{
    // Immutable deltas retain every lifecycle transition; snapshots recover expired cursors.
    internal static class DownloadControllerEvents
    {
        internal const int Capacity = 512;
        internal static readonly string Epoch = Guid.NewGuid().ToString("N");
        private static readonly object gate = new object();
        private static readonly Queue<Entry> journal = new Queue<Entry>();
        internal static Action<string, string?>? Changed;
        private static long sequence;
        internal static long Sequence { get { lock (gate) return sequence; } }
        internal sealed class Entry
        {
            internal long Sequence;
            internal string Json = String.Empty;
        }
        internal static void Publish(Func<long, string> serialize)
        {
            lock (gate)
            {
                var next = sequence + 1;
                var json = serialize(next);
                sequence = next;
                journal.Enqueue(new Entry { Sequence = next, Json = json });
                while (journal.Count > Capacity) journal.Dequeue();
                Monitor.PulseAll(gate);
            }
        }
        internal static Entry[]? Read(long after, bool wait, CancellationToken cancellation = default)
        {
            lock (gate)
            {
                if (wait && after == sequence && !cancellation.IsCancellationRequested) Monitor.Wait(gate, 20000);
                if (after > sequence || (journal.Count > 0 && after < journal.Peek().Sequence - 1)) return null;
                return journal.Where(item => item.Sequence > after).ToArray();
            }
        }
        internal static void Wake()
        {
            lock (gate) Monitor.PulseAll(gate);
        }

        // This channel accepts only WebSocket control frames; commands use authenticated HTTP.
        // Consume close/ping/pong so reconnects release their slot immediately.
        internal static void ReadControls(Stream stream, CancellationTokenSource closed)
        {
            try
            {
                while (!closed.IsCancellationRequested)
                {
                    var first = stream.ReadByte();
                    var second = stream.ReadByte();
                    if (first < 0 || second < 0) break;
                    var opcode = first & 15;
                    var length = second & 127;
                    if ((first & 0xf0) != 0x80 || (second & 128) == 0 || length > 125
                        || (opcode != 8 && opcode != 9 && opcode != 10)) break;
                    var mask = new byte[4];
                    var payload = new byte[length];
                    ReadExactly(stream, mask);
                    ReadExactly(stream, payload);
                    for (var i = 0; i < length; i++) payload[i] ^= mask[i % 4];
                    if (opcode == 8 && length == 1) break;
                    if (opcode == 8 || opcode == 9)
                    {
                        lock (stream)
                        {
                            stream.WriteByte((byte)(0x80 | (opcode == 9 ? 10 : 8)));
                            stream.WriteByte((byte)length);
                            stream.Write(payload, 0, length);
                            stream.Flush();
                        }
                    }
                    if (opcode == 8) break;
                }
            }
            catch (IOException) { }
            catch (ObjectDisposedException) { }
            finally
            {
                closed.Cancel();
                Wake();
            }
        }

        private static void ReadExactly(Stream stream, byte[] buffer)
        {
            var offset = 0;
            while (offset < buffer.Length)
            {
                var read = stream.Read(buffer, offset, buffer.Length - offset);
                if (read == 0) throw new EndOfStreamException();
                offset += read;
            }
        }

        internal static void WriteText(Stream stream, string json)
        {
            lock (stream)
            {
                var bytes = Encoding.UTF8.GetBytes(json);
                stream.WriteByte(0x81);
                if (bytes.Length < 126) stream.WriteByte((byte)bytes.Length);
                else if (bytes.Length <= ushort.MaxValue)
                {
                    stream.WriteByte(126);
                    stream.WriteByte((byte)(bytes.Length >> 8));
                    stream.WriteByte((byte)bytes.Length);
                }
                else
                {
                    stream.WriteByte(127);
                    for (var shift = 56; shift >= 0; shift -= 8)
                        stream.WriteByte((byte)((long)bytes.Length >> shift));
                }
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush();
            }
        }
    }
}
