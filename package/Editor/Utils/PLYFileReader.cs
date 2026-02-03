// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Unity.Collections;

namespace GaussianSplatting.Editor.Utils
{
    public static class PLYFileReader
    {
        public static void ReadFileHeader(string filePath, out int vertexCount, out int vertexStride, out List<(string, ElementType)> attrs)
        {
            vertexCount = 0;
            vertexStride = 0;
            attrs = new List<(string, ElementType)>();
            if (!File.Exists(filePath))
                return;
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            ReadHeaderImpl(filePath, out vertexCount, out vertexStride, out attrs, fs);
        }

        static void ReadHeaderImpl(string filePath, out int vertexCount, out int vertexStride, out List<(string, ElementType)> attrs, FileStream fs)
        {
            if (fs.Length >= 2 * 1024 * 1024 * 1024L)
                throw new IOException($"PLY {filePath} read error: currently files larger than 2GB are not supported");

            vertexCount = 0;
            vertexStride = 0;
            attrs = new List<(string, ElementType)>();
            const int kMaxHeaderLines = 9000;
            bool got_binary_le = false;
            for (int lineIdx = 0; lineIdx < kMaxHeaderLines; ++lineIdx)
            {
                var line = ReadLine(fs);
                if (line == "end_header" || line.Length == 0)
                    break;
                var tokens = line.Split(' ');
                if (tokens.Length == 3 && tokens[0] == "format" && tokens[1] == "binary_little_endian" && tokens[2] == "1.0")
                    got_binary_le = true;
                if (tokens.Length == 3 && tokens[0] == "element" && tokens[1] == "vertex")
                    vertexCount = int.Parse(tokens[2]);
                if (tokens.Length == 3 && tokens[0] == "property")
                {
                    // 수정 포인트 1: QUEEN의 'int' 타입을 처리할 수 있도록 확장
                    ElementType type = tokens[1] switch
                    {
                        "float" => ElementType.Float,
                        "double" => ElementType.Double,
                        "uchar" => ElementType.UChar,
                        "int" => ElementType.Int, // QUEEN의 vertex_id 대응
                        _ => ElementType.None
                    };
                    vertexStride += TypeToSize(type);
                    attrs.Add((tokens[2], type));
                }
            }

            if (!got_binary_le)
            {
                throw new IOException($"PLY {filePath} not supported: needs to be binary, little endian PLY format");
            }
        }

        public static void ReadFile(string filePath, out int vertexCount, out int vertexStride, out List<(string, ElementType)> attrs, out NativeArray<byte> vertices)
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            ReadHeaderImpl(filePath, out vertexCount, out vertexStride, out attrs, fs);

            vertices = new NativeArray<byte>(vertexCount * vertexStride, Allocator.Persistent);
            var readBytes = fs.Read(vertices);
            if (readBytes != vertices.Length)
                throw new IOException($"PLY {filePath} read error, expected {vertices.Length} data bytes got {readBytes}");
        }

        public enum ElementType
        {
            None,
            Float,
            Double,
            UChar,
            Int // 수정 포인트 2: Int 추가
        }

        public static int TypeToSize(ElementType t)
        {
            return t switch
            {
                ElementType.None => 0,
                ElementType.Float => 4,
                ElementType.Double => 8,
                ElementType.UChar => 1,
                ElementType.Int => 4, // 수정 포인트 3: int 사이즈 4바이트 지정
                _ => throw new ArgumentOutOfRangeException(nameof(t), t, null)
            };
        }

        static string ReadLine(FileStream fs)
        {
            var byteBuffer = new List<byte>();
            while (true)
            {
                int b = fs.ReadByte();
                if (b == -1 || b == '\n')
                    break;
                byteBuffer.Add((byte)b);
            }
            if (byteBuffer.Count > 0 && byteBuffer.Last() == '\r')
                byteBuffer.RemoveAt(byteBuffer.Count-1);
            return Encoding.UTF8.GetString(byteBuffer.ToArray());
        }
    }
}