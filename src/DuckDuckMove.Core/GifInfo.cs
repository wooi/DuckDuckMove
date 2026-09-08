using System.Buffers.Binary;

namespace DuckDuckMove.Core;

public sealed record GifInfo(int Width, int Height, int Frames, long Bytes)
{
    public const int MaxBytes = 20 * 1024 * 1024;
    public static GifInfo Inspect(byte[] data)
    {
        if (data.Length < 14 || data.Length > MaxBytes ||
            !(data.AsSpan(0, 6).SequenceEqual("GIF89a"u8) || data.AsSpan(0, 6).SequenceEqual("GIF87a"u8)))
            throw new InvalidDataException("请选择有效的 GIF 文件，大小不超过 20 MB。");
        int w = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(6, 2));
        int h = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(8, 2));
        if (w is < 1 or > 2048 || h is < 1 or > 2048)
            throw new InvalidDataException("GIF 宽高须在 1–2048 像素之间。");
        int position = 13, frames = 0;
        void Skip(int count)
        {
            if (count < 0 || position > data.Length - count) throw new InvalidDataException("GIF 文件不完整。");
            position += count;
        }
        void Blocks()
        {
            while (true)
            {
                if (position >= data.Length) throw new InvalidDataException("GIF 数据块不完整。");
                int count = data[position++];
                if (count == 0) break;
                Skip(count);
            }
        }
        if ((data[10] & 128) != 0) Skip(3 * (1 << ((data[10] & 7) + 1)));
        while (position < data.Length)
        {
            byte tag = data[position++];
            if (tag == 0x3b)
            {
                if (frames < 2) throw new InvalidDataException("这张 GIF 只有一帧，请选择动态 GIF。");
                return new(w, h, frames, data.Length);
            }
            if (tag == 0x21) { Skip(1); Blocks(); continue; }
            if (tag != 0x2c) throw new InvalidDataException("GIF 包含无效数据块。");
            int descriptor = position;
            Skip(9);
            int fw = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(descriptor + 4, 2));
            int fh = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(descriptor + 6, 2));
            int left = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(descriptor, 2));
            int top = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(descriptor + 2, 2));
            if (fw < 1 || fh < 1 || left + fw > w || top + fh > h) throw new InvalidDataException("GIF 帧尺寸无效。");
            byte packed = data[descriptor + 8];
            if ((packed & 128) != 0) Skip(3 * (1 << ((packed & 7) + 1)));
            if (position >= data.Length || data[position] is < 2 or > 8) throw new InvalidDataException("GIF 编码参数无效。");
            Skip(1); Blocks(); frames++;
            if (frames > 500 || (long)w * h * frames > 120_000_000)
                throw new InvalidDataException("动图过长或分辨率过高，请缩小后再试。");
        }
        throw new InvalidDataException("GIF 缺少结束标记。");
    }
}
