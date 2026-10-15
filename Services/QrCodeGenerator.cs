using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;

namespace StreloReminder.Services
{
    /// <summary>
    /// Автономный генератор стандартных QR-кодов ISO/IEC 18004 (Byte Mode, ECC Level L, версии 1..20)
    /// без сторонних библиотек. Создаёт сканируемое изображение Bitmap для быстрой привязки Android-устройства.
    /// </summary>
    public static class QrCodeGenerator
    {
        // Общее число кодовых слов данных для версий 1..20 (уровень коррекции L)
        private static readonly int[] DataCodewordsLevelL = new int[]
        {
            0,
            19, 34, 55, 80, 108, 136, 156, 194, 232, 274,
            324, 370, 428, 461, 523, 589, 647, 721, 795, 861
        };

        // Число кодовых слов коррекции ошибок (ECC) на один блок для версий 1..20 (уровень L)
        private static readonly int[] EccCodewordsPerBlockLevelL = new int[]
        {
            0,
            7, 10, 15, 20, 26, 18, 20, 24, 30, 18,
            20, 24, 26, 30, 22, 24, 28, 30, 28, 28
        };

        // Общее число блоков (группа 1 + группа 2) для версий 1..20 (уровень L)
        private static readonly int[] NumErrorCorrectionBlocksLevelL = new int[]
        {
            0,
            1, 1, 1, 1, 1, 2, 2, 2, 2, 4,
            4, 4, 4, 4, 6, 6, 6, 6, 7, 8
        };

        // Координаты центров выравнивающих шаблонов (Alignment Patterns) для версий 1..20
        private static readonly int[][] AlignmentPatternPositions = new int[][]
        {
            new int[0],
            new int[0],
            new int[] { 6, 18 },
            new int[] { 6, 22 },
            new int[] { 6, 26 },
            new int[] { 6, 30 },
            new int[] { 6, 34 },
            new int[] { 6, 22, 38 },
            new int[] { 6, 24, 42 },
            new int[] { 6, 26, 46 },
            new int[] { 6, 28, 50 },
            new int[] { 6, 30, 54 },
            new int[] { 6, 32, 58 },
            new int[] { 6, 34, 62 },
            new int[] { 6, 26, 46, 66 },
            new int[] { 6, 26, 48, 70 },
            new int[] { 6, 26, 50, 74 },
            new int[] { 6, 30, 54, 78 },
            new int[] { 6, 30, 56, 82 },
            new int[] { 6, 30, 58, 86 },
            new int[] { 6, 34, 62, 90 }
        };

        public static Bitmap GenerateQrBitmap(string text, int targetPixelSize)
        {
            if (string.IsNullOrEmpty(text))
            {
                text = "STRELO";
            }

            bool[,] modules = EncodeTextToQrMatrix(text);
            int matrixSize = modules.GetLength(0);
            int quietZone = 3;
            int totalModules = matrixSize + quietZone * 2;
            int scale = Math.Max(2, targetPixelSize / totalModules);
            int bitmapSize = Math.Max(targetPixelSize, totalModules * scale);
            int offset = (bitmapSize - matrixSize * scale) / 2;

            Bitmap bmp = new Bitmap(bitmapSize, bitmapSize);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.White);
                using (Brush darkBrush = new SolidBrush(Color.FromArgb(15, 23, 42)))
                {
                    for (int r = 0; r < matrixSize; r++)
                    {
                        for (int c = 0; c < matrixSize; c++)
                        {
                            if (modules[r, c])
                            {
                                g.FillRectangle(
                                    darkBrush,
                                    offset + c * scale,
                                    offset + r * scale,
                                    scale,
                                    scale);
                            }
                        }
                    }
                }
            }
            return bmp;
        }

        public static bool[,] EncodeTextToQrMatrix(string text)
        {
            byte[] payload = Encoding.UTF8.GetBytes(text ?? string.Empty);

            int version = 1;
            while (version <= 20)
            {
                int capacityBits = DataCodewordsLevelL[version] * 8;
                int countBits = version <= 9 ? 8 : 16;
                int requiredBits = 4 + countBits + payload.Length * 8;
                if (requiredBits <= capacityBits)
                {
                    break;
                }
                version++;
            }

            if (version > 20)
            {
                version = 20;
                int maxBytes = DataCodewordsLevelL[20] - 3;
                byte[] truncated = new byte[maxBytes];
                Array.Copy(payload, truncated, maxBytes);
                payload = truncated;
            }

            int totalDataCodewords = DataCodewordsLevelL[version];
            List<int> bits = new List<int>(totalDataCodewords * 8);

            // Режим Byte Mode = 0100
            AppendBits(bits, 0x4, 4);
            AppendBits(bits, payload.Length, version <= 9 ? 8 : 16);
            for (int i = 0; i < payload.Length; i++)
            {
                AppendBits(bits, payload[i], 8);
            }

            // Терминатор до 4 нулевых бит
            int capacityTotalBits = totalDataCodewords * 8;
            int terminatorLen = Math.Min(4, capacityTotalBits - bits.Count);
            AppendBits(bits, 0, terminatorLen);

            // Выравнивание по границе байта
            while (bits.Count % 8 != 0)
            {
                bits.Add(0);
            }

            // Чередующиеся байты заполнения 0xEC, 0x11
            int padToggle = 0;
            while (bits.Count < capacityTotalBits)
            {
                AppendBits(bits, padToggle == 0 ? 0xEC : 0x11, 8);
                padToggle ^= 1;
            }

            byte[] dataCodewords = new byte[totalDataCodewords];
            for (int i = 0; i < totalDataCodewords; i++)
            {
                int b = 0;
                for (int bit = 0; bit < 8; bit++)
                {
                    b = (b << 1) | bits[i * 8 + bit];
                }
                dataCodewords[i] = (byte)b;
            }

            byte[] allCodewords = AddReedSolomonAndInterleave(dataCodewords, version);
            int size = 17 + 4 * version;
            bool[,] modules = new bool[size, size];
            bool[,] isFunction = new bool[size, size];

            DrawFunctionPatterns(modules, isFunction, version);
            DrawCodewords(modules, isFunction, allCodewords);

            // Применяем маску 0: (r + c) % 2 == 0
            int mask = 0;
            for (int r = 0; r < size; r++)
            {
                for (int c = 0; c < size; c++)
                {
                    if (!isFunction[r, c] && ((r + c) % 2 == 0))
                    {
                        modules[r, c] = !modules[r, c];
                    }
                }
            }

            DrawFormatBits(modules, isFunction, mask);
            return modules;
        }

        private static void AppendBits(List<int> list, int val, int numBits)
        {
            for (int i = numBits - 1; i >= 0; i--)
            {
                list.Add((val >> i) & 1);
            }
        }

        private static byte[] AddReedSolomonAndInterleave(byte[] data, int version)
        {
            int numBlocks = NumErrorCorrectionBlocksLevelL[version];
            int eccLen = EccCodewordsPerBlockLevelL[version];
            int totalData = DataCodewordsLevelL[version];

            int shortBlockDataLen = totalData / numBlocks;
            int numLongBlocks = totalData % numBlocks;
            int numShortBlocks = numBlocks - numLongBlocks;

            byte[][] dataBlocks = new byte[numBlocks][];
            byte[][] eccBlocks = new byte[numBlocks][];
            byte[] rsDivisor = ComputeReedSolomonDivisor(eccLen);

            int offset = 0;
            for (int i = 0; i < numBlocks; i++)
            {
                int datLen = shortBlockDataLen + (i < numShortBlocks ? 0 : 1);
                byte[] block = new byte[datLen];
                Array.Copy(data, offset, block, 0, datLen);
                offset += datLen;

                dataBlocks[i] = block;
                eccBlocks[i] = ComputeReedSolomonRemainder(block, rsDivisor);
            }

            List<byte> result = new List<byte>(totalData + numBlocks * eccLen);
            int maxDataBlockLen = shortBlockDataLen + (numLongBlocks > 0 ? 1 : 0);
            for (int i = 0; i < maxDataBlockLen; i++)
            {
                for (int j = 0; j < numBlocks; j++)
                {
                    if (i < dataBlocks[j].Length)
                    {
                        result.Add(dataBlocks[j][i]);
                    }
                }
            }

            for (int i = 0; i < eccLen; i++)
            {
                for (int j = 0; j < numBlocks; j++)
                {
                    result.Add(eccBlocks[j][i]);
                }
            }

            return result.ToArray();
        }

        private static byte[] ComputeReedSolomonDivisor(int degree)
        {
            byte[] result = new byte[degree];
            result[degree - 1] = 1;
            byte root = 1;
            for (int i = 0; i < degree; i++)
            {
                for (int j = 0; j < degree; j++)
                {
                    result[j] = GfMultiply(result[j], root);
                    if (j + 1 < degree)
                    {
                        result[j] ^= result[j + 1];
                    }
                }
                root = GfMultiply(root, 0x02);
            }
            return result;
        }

        private static byte[] ComputeReedSolomonRemainder(byte[] data, byte[] divisor)
        {
            byte[] result = new byte[divisor.Length];
            for (int i = 0; i < data.Length; i++)
            {
                byte factor = (byte)(data[i] ^ result[0]);
                Array.Copy(result, 1, result, 0, result.Length - 1);
                result[result.Length - 1] = 0;
                for (int j = 0; j < result.Length; j++)
                {
                    result[j] ^= GfMultiply(divisor[j], factor);
                }
            }
            return result;
        }

        private static byte GfMultiply(byte x, byte y)
        {
            int z = 0;
            for (int i = 7; i >= 0; i--)
            {
                z = (z << 1) ^ ((z >> 7) * 0x11D);
                z ^= ((y >> i) & 1) * x;
            }
            return (byte)z;
        }

        private static void DrawFunctionPatterns(bool[,] modules, bool[,] isFunction, int version)
        {
            int size = modules.GetLength(0);

            // Полосы синхронизации (Timing patterns)
            for (int i = 0; i < size; i++)
            {
                SetFunctionModule(modules, isFunction, 6, i, i % 2 == 0);
                SetFunctionModule(modules, isFunction, i, 6, i % 2 == 0);
            }

            // Поисковые шаблоны в 3 углах (Finder patterns)
            DrawFinderPattern(modules, isFunction, 3, 3);
            DrawFinderPattern(modules, isFunction, 3, size - 4);
            DrawFinderPattern(modules, isFunction, size - 4, 3);

            // Выравнивающие шаблоны (Alignment patterns)
            int[] alignPos = AlignmentPatternPositions[version];
            int numAlign = alignPos.Length;
            for (int i = 0; i < numAlign; i++)
            {
                for (int j = 0; j < numAlign; j++)
                {
                    if ((i == 0 && j == 0) || (i == 0 && j == numAlign - 1) || (i == numAlign - 1 && j == 0))
                        continue;
                    DrawAlignmentPattern(modules, isFunction, alignPos[i], alignPos[j]);
                }
            }

            // Резервируем биты формата
            DrawFormatBits(modules, isFunction, 0);

            // Информация о версии для версий >= 7
            if (version >= 7)
            {
                int rem = version;
                for (int i = 0; i < 12; i++)
                {
                    rem = (rem << 1) ^ ((rem >> 11) * 0x1F25);
                }
                int bits = (version << 12) | rem;
                for (int i = 0; i < 18; i++)
                {
                    bool bit = ((bits >> i) & 1) != 0;
                    int a = size - 11 + i % 3;
                    int b = i / 3;
                    SetFunctionModule(modules, isFunction, a, b, bit);
                    SetFunctionModule(modules, isFunction, b, a, bit);
                }
            }
        }

        private static void DrawFormatBits(bool[,] modules, bool[,] isFunction, int mask)
        {
            int size = modules.GetLength(0);
            // Уровень L = 01 в формате QR
            int data = (1 << 3) | mask;
            int rem = data;
            for (int i = 0; i < 10; i++)
            {
                rem = (rem << 1) ^ ((rem >> 9) * 0x537);
            }
            int bits = ((data << 10) | rem) ^ 0x5412;

            // Первая копия бит формата (вокруг верхнего левого поискового шаблона)
            for (int i = 0; i <= 5; i++)
            {
                SetFunctionModule(modules, isFunction, i, 8, ((bits >> i) & 1) != 0);
            }
            SetFunctionModule(modules, isFunction, 7, 8, ((bits >> 6) & 1) != 0);
            SetFunctionModule(modules, isFunction, 8, 8, ((bits >> 7) & 1) != 0);
            SetFunctionModule(modules, isFunction, 8, 7, ((bits >> 8) & 1) != 0);
            for (int i = 9; i < 15; i++)
            {
                SetFunctionModule(modules, isFunction, 8, 14 - i, ((bits >> i) & 1) != 0);
            }

            // Вторая копия бит формата (вдоль правого верхнего и левого нижнего шаблонов)
            for (int i = 0; i < 8; i++)
            {
                SetFunctionModule(modules, isFunction, 8, size - 1 - i, ((bits >> i) & 1) != 0);
            }
            for (int i = 8; i < 15; i++)
            {
                SetFunctionModule(modules, isFunction, size - 15 + i, 8, ((bits >> i) & 1) != 0);
            }

            // Постоянный тёмный модуль (Dark module)
            SetFunctionModule(modules, isFunction, size - 8, 8, true);
        }

        private static void DrawFinderPattern(bool[,] modules, bool[,] isFunction, int centerR, int centerC)
        {
            int size = modules.GetLength(0);
            for (int dr = -4; dr <= 4; dr++)
            {
                for (int dc = -4; dc <= 4; dc++)
                {
                    int r = centerR + dr;
                    int c = centerC + dc;
                    if (r >= 0 && r < size && c >= 0 && c < size)
                    {
                        int dist = Math.Max(Math.Abs(dr), Math.Abs(dc));
                        bool dark = (dist != 2 && dist != 4);
                        SetFunctionModule(modules, isFunction, r, c, dark);
                    }
                }
            }
        }

        private static void DrawAlignmentPattern(bool[,] modules, bool[,] isFunction, int centerR, int centerC)
        {
            for (int dr = -2; dr <= 2; dr++)
            {
                for (int dc = -2; dc <= 2; dc++)
                {
                    int dist = Math.Max(Math.Abs(dr), Math.Abs(dc));
                    SetFunctionModule(modules, isFunction, centerR + dr, centerC + dc, dist != 1);
                }
            }
        }

        private static void SetFunctionModule(bool[,] modules, bool[,] isFunction, int r, int c, bool dark)
        {
            modules[r, c] = dark;
            isFunction[r, c] = true;
        }

        private static void DrawCodewords(bool[,] modules, bool[,] isFunction, byte[] allCodewords)
        {
            int size = modules.GetLength(0);
            int bitIdx = 0;
            int totalBits = allCodewords.Length * 8;

            for (int right = size - 1; right >= 1; right -= 2)
            {
                if (right == 6)
                {
                    right = 5;
                }
                for (int vert = 0; vert < size; vert++)
                {
                    for (int j = 0; j < 2; j++)
                    {
                        int c = right - j;
                        bool upward = ((right + 1) & 2) == 0;
                        int r = upward ? (size - 1 - vert) : vert;
                        if (!isFunction[r, c] && bitIdx < totalBits)
                        {
                            bool dark = ((allCodewords[bitIdx >> 3] >> (7 - (bitIdx & 7))) & 1) != 0;
                            modules[r, c] = dark;
                            bitIdx++;
                        }
                    }
                }
            }
        }
    }
}
