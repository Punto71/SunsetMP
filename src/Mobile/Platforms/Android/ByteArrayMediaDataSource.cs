using Android.Media;

namespace SunsetMP;

/// <summary>Источник для MediaPlayer, читающий трек из массива в памяти (с поддержкой перемотки).</summary>
class ByteArrayMediaDataSource : MediaDataSource
{
    readonly byte[] _data;

    public ByteArrayMediaDataSource(byte[] data) => _data = data;

    public override long Size => _data.Length;

    public override int ReadAt(long position, byte[]? buffer, int offset, int size)
    {
        if (buffer == null || position >= _data.Length) return -1;
        var count = (int)Math.Min(size, _data.Length - position);
        Buffer.BlockCopy(_data, (int)position, buffer, offset, count);
        return count;
    }

    public override void Close() { }
}
