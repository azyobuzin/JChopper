using System;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Formatting;

namespace JChopper
{
    static class InternalHelper
    {
        private static FieldInfo GetField(string name)
        {
            return typeof(InternalHelper).GetRuntimeField(name);
        }

        private static MemberExpression FieldExpr(string name)
        {
            return Expression.Field(null, GetField(name));
        }

        private static MethodInfo GetMethod(string name)
        {
            return typeof(InternalHelper).GetTypeInfo().GetDeclaredMethod(name);
        }

        // Fields are public for GetRuntimeField

        public static readonly Type[] EmptyTypeArray = new Type[0];

        public static readonly Format.Parsed DefaultFormat = default(Format.Parsed);
        public static readonly MemberExpression DefaultFormatExpr = FieldExpr(nameof(DefaultFormat));

        public static readonly Format.Parsed IntegerFormat = Format.Parse('D');
        public static readonly MemberExpression IntegerFormatExpr = FieldExpr(nameof(IntegerFormat));

        public static readonly MethodInfo FloatToStringMethod = typeof(float).GetRuntimeMethod(nameof(float.ToString), new[] { typeof(string), typeof(IFormatProvider) });
        public static readonly MethodInfo DoubleToStringMethod = typeof(double).GetRuntimeMethod(nameof(double.ToString), new[] { typeof(string), typeof(IFormatProvider) });
        public static readonly MethodInfo DecimalToStringMethod = typeof(decimal).GetRuntimeMethod(nameof(double.ToString), new[] { typeof(string), typeof(IFormatProvider) });
        public static readonly MethodInfo CharToStringStaticMethod = typeof(char).GetRuntimeMethod(nameof(char.ToString), new[] { typeof(char) });

        public static readonly FieldInfo InvariantCultureField = typeof(CultureInfo).GetRuntimeField(nameof(CultureInfo.InvariantCulture));
        public static readonly MemberExpression InvariantCultureExpr = Expression.Field(null, InvariantCultureField);

        public static readonly PropertyInfo IsUtf16Property = typeof(FormattingData).GetTypeInfo().DeclaredProperties.First(x => x.Name == "IsUtf16");

        public static readonly MethodInfo DisposeMethod = typeof(IDisposable).GetRuntimeMethod(nameof(IDisposable.Dispose), EmptyTypeArray);

        private static readonly byte[] u000Utf16 = Encoding.Unicode.GetBytes("\\u000");
        private static readonly byte[] u001Utf16 = Encoding.Unicode.GetBytes("\\u001");
        private static readonly byte[] u000Utf8 = Encoding.UTF8.GetBytes("\\u000");
        private static readonly byte[] u001Utf8 = Encoding.UTF8.GetBytes("\\u001");

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void RequireBuffer<TFormatter>(ref Span<byte> buffer, ref int bytesWritten, TFormatter formatter, int requiredBytes)
            where TFormatter : IFormatter
        {
            if (buffer.Length >= requiredBytes)
                return;

            // Commit
            if (bytesWritten > 0)
            {
                formatter.CommitBytes(bytesWritten);
                bytesWritten = 0;
                buffer = formatter.FreeBuffer;
            }

            // Resize
            while (buffer.Length < requiredBytes)
            {
                formatter.ResizeBuffer();
                buffer = formatter.FreeBuffer;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Span<byte> RequireBuffer<TFormatter>(TFormatter formatter, int requiredBytes)
            where TFormatter : IFormatter
        {
            Span<byte> buffer;
            while ((buffer = formatter.FreeBuffer).Length < requiredBytes)
                formatter.ResizeBuffer();
            return buffer;
        }

        public static unsafe void WriteStringToUtf16<TFormatter>(TFormatter formatter, string value) where TFormatter : IFormatter
        {
            var buffer = RequireBuffer(formatter, 4);

            buffer[0] = (byte)'"';
            buffer[1] = 0;
            buffer = buffer.Slice(2);
            var bytesWritten = 2;

            fixed (char* pChars = value)
            {
                var start = 0;
                var i = 0;
                for (; i < value.Length; i++)
                {
                    var c = pChars[i];
                    int flag;
                    char x;
                    switch (c)
                    {
                        case '\u0022':
                        case '\u005C':
                            flag = 2;
                            x = c;
                            break;
                        case '\u0008':
                            flag = 2;
                            x = 'b';
                            break;
                        case '\u000C':
                            flag = 2;
                            x = 'f';
                            break;
                        case '\u000A':
                            flag = 2;
                            x = 'n';
                            break;
                        case '\u000D':
                            flag = 2;
                            x = 'r';
                            break;
                        case '\u0009':
                            flag = 2;
                            x = 't';
                            break;
                        default:
                            if (c >= '\u0000' && c <= '\u0009')
                            {
                                flag = 0;
                                x = (char)(c + 0x0030);
                            }
                            else if (c >= '\u000A' && c <= '\u000F')
                            {
                                flag = 0;
                                x = (char)(c + 0x0037);
                            }
                            else if (c >= '\u0010' && c <= '\u0019')
                            {
                                flag = 1;
                                x = (char)(c + 0x0020);
                            }
                            else if (c >= '\u001A' && c <= '\u001F')
                            {
                                flag = 1;
                                x = (char)(c + 0x0027);
                            }
                            else
                            {
                                continue;
                            }
                            break;
                    }

                    if (start < i)
                    {
                        var bytes = (i - start) * 2;
                        RequireBuffer(ref buffer, ref bytesWritten, formatter, bytes);

                        // Write
                        buffer.Set(((byte*)pChars) + (start * 2), bytes);
                        bytesWritten += bytes;
                        buffer = buffer.Slice(bytes);
                    }

                    var pBuf = (char*)buffer.UnsafePointer;
                    switch (flag)
                    {
                        case 0:
                            // \u000x
                            RequireBuffer(ref buffer, ref bytesWritten, formatter, 12);
                            buffer.Set(u000Utf16);
                            pBuf[5] = x;
                            bytesWritten += 12;
                            buffer = buffer.Slice(12);
                            break;
                        case 1:
                            // \u001x
                            RequireBuffer(ref buffer, ref bytesWritten, formatter, 12);
                            buffer.Set(u001Utf16);
                            pBuf[5] = x;
                            bytesWritten += 12;
                            buffer = buffer.Slice(12);
                            break;
                        case 2:
                            // \x
                            RequireBuffer(ref buffer, ref bytesWritten, formatter, 4);
                            pBuf[0] = '\\';
                            pBuf[1] = x;
                            bytesWritten += 4;
                            buffer = buffer.Slice(4);
                            break;
                        default:
                            throw new Exception("unreachable");
                    }

                    start = i + 1;
                }

                if (start < i)
                {
                    var bytes = (i - start) * 2;
                    RequireBuffer(ref buffer, ref bytesWritten, formatter, bytes + 2);
                    buffer.Set(((byte*)pChars) + (start * 2), bytes);
                    bytesWritten += bytes;
                    buffer = buffer.Slice(bytes);
                }
                else
                {
                    RequireBuffer(ref buffer, ref bytesWritten, formatter, 2);
                }
            }

            buffer[0] = (byte)'"';
            buffer[1] = 0;
            formatter.CommitBytes(bytesWritten + 2);
        }

        public static readonly MethodInfo WriteStringToUtf16Method = GetMethod(nameof(WriteStringToUtf16));

        public static void WriteStringToUtf8<TFormatter>(TFormatter formatter, string value) where TFormatter : IFormatter
        {
            var buffer = RequireBuffer(formatter, 2);

            buffer[0] = (byte)'"';
            buffer = buffer.Slice(1);
            var bytesWritten = 1;

            var start = 0;
            var i = 0;
            for (; i < value.Length; i++)
            {
                var c = value[i];
                int flag;
                byte x;
                switch (c)
                {
                    case '\u0022':
                    case '\u005C':
                        flag = 2;
                        x = (byte)c;
                        break;
                    case '\u0008':
                        flag = 2;
                        x = (byte)'b';
                        break;
                    case '\u000C':
                        flag = 2;
                        x = (byte)'f';
                        break;
                    case '\u000A':
                        flag = 2;
                        x = (byte)'n';
                        break;
                    case '\u000D':
                        flag = 2;
                        x = (byte)'r';
                        break;
                    case '\u0009':
                        flag = 2;
                        x = (byte)'t';
                        break;
                    default:
                        if (c >= '\u0000' && c <= '\u0009')
                        {
                            flag = 0;
                            x = (byte)(c + 0x0030);
                        }
                        else if (c >= '\u000A' && c <= '\u000F')
                        {
                            flag = 0;
                            x = (byte)(c + 0x0037);
                        }
                        else if (c >= '\u0010' && c <= '\u0019')
                        {
                            flag = 1;
                            x = (byte)(c + 0x0020);
                        }
                        else if (c >= '\u001A' && c <= '\u001F')
                        {
                            flag = 1;
                            x = (byte)(c + 0x0027);
                        }
                        else
                        {
                            continue;
                        }
                        break;
                }

                if (start < i)
                {
                    var bytes = Encoding.UTF8.GetBytes(value.ToCharArray(start, i - start));
                    RequireBuffer(ref buffer, ref bytesWritten, formatter, bytes.Length);
                    buffer.Set(bytes);
                    bytesWritten += bytes.Length;
                    buffer = buffer.Slice(bytes.Length);
                }

                switch (flag)
                {
                    case 0:
                        // \u000x
                        RequireBuffer(ref buffer, ref bytesWritten, formatter, 6);
                        buffer.Set(u000Utf8);
                        buffer[5] = x;
                        buffer = buffer.Slice(6);
                        break;
                    case 1:
                        // \u001x
                        RequireBuffer(ref buffer, ref bytesWritten, formatter, 6);
                        buffer.Set(u001Utf8);
                        buffer[5] = x;
                        buffer = buffer.Slice(6);
                        break;
                    case 2:
                        // \x
                        RequireBuffer(ref buffer, ref bytesWritten, formatter, 2);
                        buffer[0] = (byte)'\\';
                        buffer[1] = x;
                        buffer = buffer.Slice(2);
                        break;
                    default:
                        throw new Exception("unreachable");
                }

                start = i + 1;
            }

            if (start < i)
            {
                var bytes = Encoding.UTF8.GetBytes(value.ToCharArray(start, i - start));
                RequireBuffer(ref buffer, ref bytesWritten, formatter, bytes.Length + 1);
                buffer.Set(bytes);
                bytesWritten += bytes.Length;
                buffer = buffer.Slice(bytes.Length);
            }
            else
            {
                RequireBuffer(ref buffer, ref bytesWritten, formatter, 1);
            }

            buffer[0] = (byte)'"';
            formatter.CommitBytes(bytesWritten + 1);
        }

        public static readonly MethodInfo WriteStringToUtf8Method = GetMethod(nameof(WriteStringToUtf8));

        public static readonly byte[] nullUtf16 = Encoding.Unicode.GetBytes("null");
        public static readonly byte[] nullUtf8 = Encoding.UTF8.GetBytes("null");
        public static readonly byte[] trueUtf16 = Encoding.Unicode.GetBytes("true");
        public static readonly byte[] trueUtf8 = Encoding.UTF8.GetBytes("true");
        public static readonly byte[] falseUtf16 = Encoding.Unicode.GetBytes("false");
        public static readonly byte[] falseUtf8 = Encoding.UTF8.GetBytes("false");

        public static readonly MemberExpression nullUtf16Expr = FieldExpr(nameof(nullUtf16));
        public static readonly MemberExpression nullUtf8Expr = FieldExpr(nameof(nullUtf8));
        public static readonly MemberExpression trueUtf16Expr = FieldExpr(nameof(trueUtf16));
        public static readonly MemberExpression trueUtf8Expr = FieldExpr(nameof(trueUtf8));
        public static readonly MemberExpression falseUtf16Expr = FieldExpr(nameof(falseUtf16));
        public static readonly MemberExpression falseUtf8Expr = FieldExpr(nameof(falseUtf8));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteBytes<TFormatter>(TFormatter formatter, byte[] value) where TFormatter : IFormatter
        {
            RequireBuffer(formatter, value.Length).Set(value);
            formatter.CommitBytes(value.Length);
        }

        public static readonly MethodInfo WriteBytesMethod = GetMethod(nameof(WriteBytes));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteComma<TFormatter>(TFormatter formatter) where TFormatter : IFormatter
        {
            var formattingData = formatter.FormattingData;
            int bytesWritten;
            while (!formattingData.TryWriteSymbol(FormattingData.Symbol.GroupSeparator, formatter.FreeBuffer, out bytesWritten))
                formatter.ResizeBuffer();
            formatter.CommitBytes(bytesWritten);
        }

        public static readonly MethodInfo WriteCommaMethod = GetMethod(nameof(WriteComma));
    }
}
