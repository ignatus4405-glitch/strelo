package com.strelo.reminder;

import android.graphics.Bitmap;
import android.graphics.Color;

import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.List;

/**
 * Автономный генератор стандартных QR-кодов ISO/IEC 18004 (Byte Mode, ECC Level L, версии 1..20)
 * без внешних зависимостей. Создаёт Bitmap для быстрой привязки устройств.
 */
public class QrCodeGenerator {

    private static final int[] DATA_CODEWORDS_LEVEL_L = new int[]{
            0,
            19, 34, 55, 80, 108, 136, 156, 194, 232, 274,
            324, 370, 428, 461, 523, 589, 647, 721, 795, 861
    };

    private static final int[] ECC_CODEWORDS_PER_BLOCK_LEVEL_L = new int[]{
            0,
            7, 10, 15, 20, 26, 18, 20, 24, 30, 18,
            20, 24, 26, 30, 22, 24, 28, 30, 28, 28
    };

    private static final int[] NUM_ECC_BLOCKS_LEVEL_L = new int[]{
            0,
            1, 1, 1, 1, 1, 2, 2, 2, 2, 4,
            4, 4, 4, 4, 6, 6, 6, 6, 7, 8
    };

    private static final int[][] ALIGNMENT_POSITIONS = new int[][]{
            new int[0],
            new int[0],
            new int[]{6, 18},
            new int[]{6, 22},
            new int[]{6, 26},
            new int[]{6, 30},
            new int[]{6, 34},
            new int[]{6, 22, 38},
            new int[]{6, 24, 42},
            new int[]{6, 26, 46},
            new int[]{6, 28, 50},
            new int[]{6, 30, 54},
            new int[]{6, 32, 58},
            new int[]{6, 34, 62},
            new int[]{6, 26, 46, 66},
            new int[]{6, 26, 48, 70},
            new int[]{6, 26, 50, 74},
            new int[]{6, 30, 54, 78},
            new int[]{6, 30, 56, 82},
            new int[]{6, 30, 58, 86},
            new int[]{6, 34, 62, 90}
    };

    public static Bitmap generateQrBitmap(String text, int targetPixelSize) {
        if (text == null || text.isEmpty()) {
            text = "STRELO";
        }
        boolean[][] modules = encodeTextToQrMatrix(text);
        int matrixSize = modules.length;
        int quietZone = 3;
        int totalModules = matrixSize + quietZone * 2;
        int scale = Math.max(2, targetPixelSize / totalModules);
        int bmpSize = Math.max(targetPixelSize, totalModules * scale);
        int offset = (bmpSize - matrixSize * scale) / 2;

        int[] pixels = new int[bmpSize * bmpSize];
        for (int i = 0; i < pixels.length; i++) {
            pixels[i] = Color.WHITE;
        }

        int darkColor = Color.parseColor("#0F172A");
        for (int r = 0; r < matrixSize; r++) {
            for (int c = 0; c < matrixSize; c++) {
                if (modules[r][c]) {
                    int startY = offset + r * scale;
                    int startX = offset + c * scale;
                    for (int dy = 0; dy < scale; dy++) {
                        int rowOffset = (startY + dy) * bmpSize + startX;
                        for (int dx = 0; dx < scale; dx++) {
                            pixels[rowOffset + dx] = darkColor;
                        }
                    }
                }
            }
        }

        return Bitmap.createBitmap(pixels, bmpSize, bmpSize, Bitmap.Config.ARGB_8888);
    }

    public static boolean[][] encodeTextToQrMatrix(String text) {
        byte[] payload = (text != null ? text : "").getBytes(StandardCharsets.UTF_8);

        int version = 1;
        while (version <= 20) {
            int capacityBits = DATA_CODEWORDS_LEVEL_L[version] * 8;
            int countBits = version <= 9 ? 8 : 16;
            int requiredBits = 4 + countBits + payload.length * 8;
            if (requiredBits <= capacityBits) {
                break;
            }
            version++;
        }

        if (version > 20) {
            version = 20;
            int maxBytes = DATA_CODEWORDS_LEVEL_L[20] - 3;
            byte[] truncated = new byte[maxBytes];
            System.arraycopy(payload, 0, truncated, 0, maxBytes);
            payload = truncated;
        }

        int totalDataCodewords = DATA_CODEWORDS_LEVEL_L[version];
        List<Integer> bits = new ArrayList<>(totalDataCodewords * 8);

        appendBits(bits, 0x4, 4);
        appendBits(bits, payload.length, version <= 9 ? 8 : 16);
        for (byte b : payload) {
            appendBits(bits, b & 0xFF, 8);
        }

        int capacityTotalBits = totalDataCodewords * 8;
        int terminatorLen = Math.min(4, capacityTotalBits - bits.size());
        appendBits(bits, 0, terminatorLen);

        while (bits.size() % 8 != 0) {
            bits.add(0);
        }

        int padToggle = 0;
        while (bits.size() < capacityTotalBits) {
            appendBits(bits, padToggle == 0 ? 0xEC : 0x11, 8);
            padToggle ^= 1;
        }

        byte[] dataCodewords = new byte[totalDataCodewords];
        for (int i = 0; i < totalDataCodewords; i++) {
            int b = 0;
            for (int bit = 0; bit < 8; bit++) {
                b = (b << 1) | bits.get(i * 8 + bit);
            }
            dataCodewords[i] = (byte) b;
        }

        byte[] allCodewords = addReedSolomonAndInterleave(dataCodewords, version);
        int size = 17 + 4 * version;
        boolean[][] modules = new boolean[size][size];
        boolean[][] isFunction = new boolean[size][size];

        drawFunctionPatterns(modules, isFunction, version);
        drawCodewords(modules, isFunction, allCodewords);

        int mask = 0;
        for (int r = 0; r < size; r++) {
            for (int c = 0; c < size; c++) {
                if (!isFunction[r][c] && ((r + c) % 2 == 0)) {
                    modules[r][c] = !modules[r][c];
                }
            }
        }

        drawFormatBits(modules, isFunction, mask);
        return modules;
    }

    private static void appendBits(List<Integer> list, int val, int numBits) {
        for (int i = numBits - 1; i >= 0; i--) {
            list.add((val >> i) & 1);
        }
    }

    private static byte[] addReedSolomonAndInterleave(byte[] data, int version) {
        int numBlocks = NUM_ECC_BLOCKS_LEVEL_L[version];
        int eccLen = ECC_CODEWORDS_PER_BLOCK_LEVEL_L[version];
        int totalData = DATA_CODEWORDS_LEVEL_L[version];

        int shortBlockDataLen = totalData / numBlocks;
        int numLongBlocks = totalData % numBlocks;
        int numShortBlocks = numBlocks - numLongBlocks;

        byte[][] dataBlocks = new byte[numBlocks][];
        byte[][] eccBlocks = new byte[numBlocks][];
        byte[] rsDivisor = computeReedSolomonDivisor(eccLen);

        int offset = 0;
        for (int i = 0; i < numBlocks; i++) {
            int datLen = shortBlockDataLen + (i < numShortBlocks ? 0 : 1);
            byte[] block = new byte[datLen];
            System.arraycopy(data, offset, block, 0, datLen);
            offset += datLen;

            dataBlocks[i] = block;
            eccBlocks[i] = computeReedSolomonRemainder(block, rsDivisor);
        }

        byte[] result = new byte[totalData + numBlocks * eccLen];
        int idx = 0;
        int maxDataBlockLen = shortBlockDataLen + (numLongBlocks > 0 ? 1 : 0);
        for (int i = 0; i < maxDataBlockLen; i++) {
            for (int j = 0; j < numBlocks; j++) {
                if (i < dataBlocks[j].length) {
                    result[idx++] = dataBlocks[j][i];
                }
            }
        }
        for (int i = 0; i < eccLen; i++) {
            for (int j = 0; j < numBlocks; j++) {
                result[idx++] = eccBlocks[j][i];
            }
        }
        return result;
    }

    private static byte[] computeReedSolomonDivisor(int degree) {
        byte[] result = new byte[degree];
        result[degree - 1] = 1;
        byte root = 1;
        for (int i = 0; i < degree; i++) {
            for (int j = 0; j < degree; j++) {
                result[j] = gfMultiply(result[j], root);
                if (j + 1 < degree) {
                    result[j] ^= result[j + 1];
                }
            }
            root = gfMultiply(root, (byte) 0x02);
        }
        return result;
    }

    private static byte[] computeReedSolomonRemainder(byte[] data, byte[] divisor) {
        byte[] result = new byte[divisor.length];
        for (byte b : data) {
            byte factor = (byte) (b ^ result[0]);
            System.arraycopy(result, 1, result, 0, result.length - 1);
            result[result.length - 1] = 0;
            for (int j = 0; j < result.length; j++) {
                result[j] ^= gfMultiply(divisor[j], factor);
            }
        }
        return result;
    }

    private static byte gfMultiply(byte x, byte y) {
        int xx = x & 0xFF;
        int yy = y & 0xFF;
        int z = 0;
        for (int i = 7; i >= 0; i--) {
            z = (z << 1) ^ ((z >> 7) * 0x11D);
            z ^= ((yy >> i) & 1) * xx;
        }
        return (byte) z;
    }

    private static void drawFunctionPatterns(boolean[][] modules, boolean[][] isFunction, int version) {
        int size = modules.length;
        for (int i = 0; i < size; i++) {
            setFunctionModule(modules, isFunction, 6, i, i % 2 == 0);
            setFunctionModule(modules, isFunction, i, 6, i % 2 == 0);
        }

        drawFinderPattern(modules, isFunction, 3, 3);
        drawFinderPattern(modules, isFunction, 3, size - 4);
        drawFinderPattern(modules, isFunction, size - 4, 3);

        int[] alignPos = ALIGNMENT_POSITIONS[version];
        int numAlign = alignPos.length;
        for (int i = 0; i < numAlign; i++) {
            for (int j = 0; j < numAlign; j++) {
                if ((i == 0 && j == 0) || (i == 0 && j == numAlign - 1) || (i == numAlign - 1 && j == 0))
                    continue;
                drawAlignmentPattern(modules, isFunction, alignPos[i], alignPos[j]);
            }
        }

        drawFormatBits(modules, isFunction, 0);

        if (version >= 7) {
            int rem = version;
            for (int i = 0; i < 12; i++) {
                rem = (rem << 1) ^ ((rem >> 11) * 0x1F25);
            }
            int bits = (version << 12) | rem;
            for (int i = 0; i < 18; i++) {
                boolean bit = ((bits >> i) & 1) != 0;
                int a = size - 11 + i % 3;
                int b = i / 3;
                setFunctionModule(modules, isFunction, a, b, bit);
                setFunctionModule(modules, isFunction, b, a, bit);
            }
        }
    }

    private static void drawFormatBits(boolean[][] modules, boolean[][] isFunction, int mask) {
        int size = modules.length;
        int data = (1 << 3) | mask;
        int rem = data;
        for (int i = 0; i < 10; i++) {
            rem = (rem << 1) ^ ((rem >> 9) * 0x537);
        }
        int bits = ((data << 10) | rem) ^ 0x5412;

        for (int i = 0; i <= 5; i++) {
            setFunctionModule(modules, isFunction, i, 8, ((bits >> i) & 1) != 0);
        }
        setFunctionModule(modules, isFunction, 7, 8, ((bits >> 6) & 1) != 0);
        setFunctionModule(modules, isFunction, 8, 8, ((bits >> 7) & 1) != 0);
        setFunctionModule(modules, isFunction, 8, 7, ((bits >> 8) & 1) != 0);
        for (int i = 9; i < 15; i++) {
            setFunctionModule(modules, isFunction, 8, 14 - i, ((bits >> i) & 1) != 0);
        }

        for (int i = 0; i < 8; i++) {
            setFunctionModule(modules, isFunction, 8, size - 1 - i, ((bits >> i) & 1) != 0);
        }
        for (int i = 8; i < 15; i++) {
            setFunctionModule(modules, isFunction, size - 15 + i, 8, ((bits >> i) & 1) != 0);
        }
        setFunctionModule(modules, isFunction, size - 8, 8, true);
    }

    private static void drawFinderPattern(boolean[][] modules, boolean[][] isFunction, int centerR, int centerC) {
        int size = modules.length;
        for (int dr = -4; dr <= 4; dr++) {
            for (int dc = -4; dc <= 4; dc++) {
                int r = centerR + dr;
                int c = centerC + dc;
                if (r >= 0 && r < size && c >= 0 && c < size) {
                    int dist = Math.max(Math.abs(dr), Math.abs(dc));
                    setFunctionModule(modules, isFunction, r, c, dist != 2 && dist != 4);
                }
            }
        }
    }

    private static void drawAlignmentPattern(boolean[][] modules, boolean[][] isFunction, int centerR, int centerC) {
        for (int dr = -2; dr <= 2; dr++) {
            for (int dc = -2; dc <= 2; dc++) {
                int dist = Math.max(Math.abs(dr), Math.abs(dc));
                setFunctionModule(modules, isFunction, centerR + dr, centerC + dc, dist != 1);
            }
        }
    }

    private static void setFunctionModule(boolean[][] modules, boolean[][] isFunction, int r, int c, boolean dark) {
        modules[r][c] = dark;
        isFunction[r][c] = true;
    }

    private static void drawCodewords(boolean[][] modules, boolean[][] isFunction, byte[] allCodewords) {
        int size = modules.length;
        int bitIdx = 0;
        int totalBits = allCodewords.length * 8;

        for (int right = size - 1; right >= 1; right -= 2) {
            if (right == 6) {
                right = 5;
            }
            for (int vert = 0; vert < size; vert++) {
                for (int j = 0; j < 2; j++) {
                    int c = right - j;
                    boolean upward = ((right + 1) & 2) == 0;
                    int r = upward ? (size - 1 - vert) : vert;
                    if (!isFunction[r][c] && bitIdx < totalBits) {
                        boolean dark = (((allCodewords[bitIdx >> 3] & 0xFF) >> (7 - (bitIdx & 7))) & 1) != 0;
                        modules[r][c] = dark;
                        bitIdx++;
                    }
                }
            }
        }
    }
}
