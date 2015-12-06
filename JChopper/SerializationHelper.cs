using System;
using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Text.Utf16;
using System.Text.Utf8;
using JChopper.Writers;

namespace JChopper
{
    public static class SerializationHelper
    {
        private static MemberExpression MemberExpr<T>(Expression<Func<T>> expr)
        {
            return ((MemberExpression)expr.Body);
        }

        private static MethodInfo GetMethod(string name)
        {
            return typeof(SerializationHelper).GetTypeInfo().GetDeclaredMethod(name);
        }

        internal static readonly ConstantExpression CommaConst = Expression.Constant((byte)',');
        internal static readonly ConstantExpression StartArrayConst = Expression.Constant((byte)'[');
        internal static readonly ConstantExpression EndArrayConst = Expression.Constant((byte)']');
        internal static readonly ConstantExpression EndObjectConst = Expression.Constant((byte)'}');

        internal static readonly MethodInfo FloatToStringMethod = typeof(float).GetRuntimeMethod(nameof(float.ToString), new[] { typeof(string), typeof(IFormatProvider) });
        internal static readonly MethodInfo DoubleToStringMethod = typeof(double).GetRuntimeMethod(nameof(double.ToString), new[] { typeof(string), typeof(IFormatProvider) });
        internal static readonly MethodInfo DecimalToStringMethod = typeof(decimal).GetRuntimeMethod(nameof(double.ToString), new[] { typeof(string), typeof(IFormatProvider) });
        internal static readonly MethodInfo CharToStringStaticMethod = typeof(char).GetRuntimeMethod(nameof(char.ToString), new[] { typeof(char) });

        internal static readonly MemberExpression InvariantCultureExpr = MemberExpr(() => CultureInfo.InvariantCulture);

        internal static readonly MethodInfo WriteBytesMethod = typeof(IWriter).GetRuntimeMethod(nameof(IWriter.Write), new[] { typeof(byte[]) });
        internal static readonly MethodInfo WriteByteMethod = typeof(IWriter).GetRuntimeMethod(nameof(IWriter.Write), new[] { typeof(byte) });

        internal static readonly MethodInfo MoveNextMethod = typeof(IEnumerator).GetRuntimeMethod(nameof(IEnumerator.MoveNext), new Type[0]);
        internal static readonly MethodInfo DisposeMethod = typeof(IDisposable).GetRuntimeMethod(nameof(IDisposable.Dispose), new Type[0]);

        internal static readonly MethodInfo SerializeMethodDefinition = typeof(IJsonSerializer).GetTypeInfo().GetDeclaredMethod("Serialize");

        private const int MaxUtf8CodePointBytes = 4;

        public static void AppendChar(this IWriter writer, char value)
        {
            int encodedBytes;
            var success = Utf8Encoder.TryEncodeCodePoint(
                new UnicodeCodePoint(value),
                writer.GetFreeBuffer(MaxUtf8CodePointBytes).ToSpan(),
                out encodedBytes);
            Debug.Assert(success);
            writer.CommitBytes(encodedBytes);
        }

        internal static readonly MethodInfo AppendCharMethod = GetMethod(nameof(AppendChar));

        private static void AppendStringInternal(this IWriter writer, string value, int startIndex, int endIndex)
        {
            for (var i = startIndex; i <= endIndex;)
            {
                UnicodeCodePoint codePoint;
                int encodedChars;
                var success = Utf16LittleEndianEncoder.TryDecodeCodePointFromString(value, i, out codePoint, out encodedChars);
                if (!success)
                    throw new ArgumentException();
                i += encodedChars;

                int encodedBytes;
                success = Utf8Encoder.TryEncodeCodePoint(
                    codePoint,
                    writer.GetFreeBuffer(MaxUtf8CodePointBytes).ToSpan(),
                    out encodedBytes);
                Debug.Assert(success);
                writer.CommitBytes(encodedBytes);
            }
        }

        public static void AppendString(this IWriter writer, string value)
        {
            var enumerator = new Utf16LittleEndianCodePointEnumerator(value); // no need to dispose this
            while (enumerator.MoveNext())
            {
                int encodedBytes;
                var success = Utf8Encoder.TryEncodeCodePoint(
                    enumerator.Current,
                    writer.GetFreeBuffer(MaxUtf8CodePointBytes).ToSpan(),
                    out encodedBytes);
                Debug.Assert(success);
                writer.CommitBytes(encodedBytes);
            }
        }

        internal static readonly MethodInfo AppendStringMethod = GetMethod(nameof(AppendString));

        public static void AppendUInt64(this IWriter writer, ulong value)
        {
            var tmp = value;
            var count = 1;
            while (tmp >= 10)
            {
                count++;
                tmp /= 10;
            }

            var buffer = writer.GetFreeBuffer(count);
            var tmp2 = buffer.Offset + count;
            for (var i = buffer.Offset; i < tmp2; i++)
            {
                buffer.Array[i] = (byte)(value % 10 + '0');
                value /= 10;
            }

            writer.CommitBytes(count);
        }

        internal static readonly MethodInfo AppendUInt64Method = GetMethod(nameof(AppendUInt64));

        public static void AppendInt64(this IWriter writer, long value)
        {
            if (value < 0)
            {
                writer.Write((byte)'-');
                value = -value;
            }

            writer.AppendUInt64(unchecked((ulong)value));
        }

        internal static readonly MethodInfo AppendInt64Method = GetMethod(nameof(AppendInt64));

        private static readonly byte[] u000Utf8 = Encoding.UTF8.GetBytes("\\u000");
        private static readonly byte[] u001Utf8 = Encoding.UTF8.GetBytes("\\u001");

        public static void WriteStringLiteral(this IWriter writer, string value)
        {
            writer.Write((byte)'"');

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
                    writer.AppendStringInternal(value, start, i - 1);
                }

                switch (flag)
                {
                    case 0:
                        // \u000x
                        writer.Write(u000Utf8);
                        writer.Write(x);
                        break;
                    case 1:
                        // \u001x
                        writer.Write(u001Utf8);
                        writer.Write(x);
                        break;
                    case 2:
                        // \x
                        writer.Write((byte)'\\');
                        writer.Write(x);
                        break;
                    default:
                        throw new Exception("unreachable");
                }

                start = i + 1;
            }

            if (start < i)
            {
                writer.AppendStringInternal(value, start, i - 1);
            }

            writer.Write((byte)'"');
        }

        internal static readonly MethodInfo WriteStringLiteralMethod = GetMethod(nameof(WriteStringLiteral));

        public static void WriteStringLiteralUtf8(this IWriter writer, Utf8String value)
        {
            writer.Write((byte)'"');

            var start = 0;
            var i = 0;
            for (; i < value.Length; i++)
            {
                var b = value[i].Value;
                int flag;
                byte x;
                switch (b)
                {
                    case 0x22:
                    case 0x5C:
                        flag = 2;
                        x = b;
                        break;
                    case 0x08:
                        flag = 2;
                        x = (byte)'b';
                        break;
                    case 0x0C:
                        flag = 2;
                        x = (byte)'f';
                        break;
                    case 0x0A:
                        flag = 2;
                        x = (byte)'n';
                        break;
                    case 0x0D:
                        flag = 2;
                        x = (byte)'r';
                        break;
                    case 0x09:
                        flag = 2;
                        x = (byte)'t';
                        break;
                    default:
                        if (b <= 0x09)
                        {
                            flag = 0;
                            x = (byte)(b + 0x30);
                        }
                        else if (b >= 0x0A && b <= 0x0F)
                        {
                            flag = 0;
                            x = (byte)(b + 0x37);
                        }
                        else if (b >= 0x10 && b <= 0x19)
                        {
                            flag = 1;
                            x = (byte)(b + 0x20);
                        }
                        else if (b >= 0x1A && b <= 0x1F)
                        {
                            flag = 1;
                            x = (byte)(b + 0x27);
                        }
                        else
                        {
                            continue;
                        }
                        break;
                }

                if (start < i)
                {
                    var slice = value.Substring(start, i - start);
                    slice.CopyTo(writer.GetFreeBuffer(slice.Length).ToSpan());
                    writer.CommitBytes(slice.Length);
                }

                switch (flag)
                {
                    case 0:
                        // \u000x
                        writer.Write(u000Utf8);
                        writer.Write(x);
                        break;
                    case 1:
                        // \u001x
                        writer.Write(u001Utf8);
                        writer.Write(x);
                        break;
                    case 2:
                        // \x
                        writer.Write((byte)'\\');
                        writer.Write(x);
                        break;
                    default:
                        throw new Exception("unreachable");
                }

                start = i + 1;
            }

            if (start < i)
            {
                var slice = value.Substring(start, i - start);
                slice.CopyTo(writer.GetFreeBuffer(slice.Length).ToSpan());
                writer.CommitBytes(slice.Length);
            }

            writer.Write((byte)'"');
        }

        internal static readonly MethodInfo WriteStringLiteralUtf8Method = GetMethod(nameof(WriteStringLiteralUtf8));

        private static readonly byte[] nullUtf8 = Encoding.UTF8.GetBytes("null");
        private static readonly byte[] trueUtf8 = Encoding.UTF8.GetBytes("true");
        private static readonly byte[] falseUtf8 = Encoding.UTF8.GetBytes("false");
        private static readonly byte[] emptyObjectUtf8 = Encoding.UTF8.GetBytes("{}");

        internal static readonly MemberExpression nullUtf8Expr = MemberExpr(() => nullUtf8);
        internal static readonly MemberExpression trueUtf8Expr = MemberExpr(() => trueUtf8);
        internal static readonly MemberExpression falseUtf8Expr = MemberExpr(() => falseUtf8);
        internal static readonly MemberExpression emptyObjectUtf8Expr = MemberExpr(() => emptyObjectUtf8);

        internal class MemberSerializer
        {
            public Expression WriteNameExpr;
            public Expression GetValueExpr;
            public Type CustomSerializer;
        }

        internal static MemberSerializer[] CreateMemberSerializers(Expression obj, ParameterExpression writer)
        {
            return obj.Type.GetRuntimeProperties()
                .Where(x => x.CanRead && !x.GetMethod.IsStatic && (x.GetMethod.IsPublic || x.IsDefined(typeof(DataMemberAttribute))) && x.GetIndexParameters().Length == 0)
                .Select(x => Tuple.Create(x as MemberInfo, Expression.Property(obj, x)))
                .Concat(
                    obj.Type.GetRuntimeFields().Where(x => !x.IsStatic && (x.IsPublic || x.IsDefined(typeof(DataMemberAttribute))))
                    .Select(x => Tuple.Create(x as MemberInfo, Expression.Field(obj, x)))
                )
                .Where(x => !x.Item1.IsDefined(typeof(IgnoreDataMemberAttribute)))
                .Select(x =>
                {
                    //TODO?: Supporting EmitDefaultValue is difficult...

                    var dataMemberAttr = x.Item1.GetCustomAttribute<DataMemberAttribute>();
                    var customSerializer = x.Item1.GetCustomAttribute<CustomSerializerAttribute>()?.CustomSerializerType;
                    return dataMemberAttr == null
                        ? Tuple.Create(-1, x.Item1.Name, x.Item2, customSerializer)
                        : Tuple.Create(dataMemberAttr.Order, dataMemberAttr.Name ?? x.Item1.Name, x.Item2, customSerializer);
                })
                .OrderBy(x => x.Item1)
                .ThenBy(x => x.Item2)
                .Select((x, i) =>
                {
                    // nameBytes
                    // { '{' or ',', ... escaped string ..., ':' }
                    byte[] nameBytes;

                    using (var memWriter = new MemoryWriter())
                    {
                        WriteStringLiteral(memWriter, x.Item2);

                        var buffer = memWriter.GetBuffer();
                        nameBytes = new byte[buffer.Count + 2];
                        nameBytes[0] = i == 0 ? (byte)'{' : (byte)',';
                        nameBytes[nameBytes.Length - 1] = (byte)':';
                        Buffer.BlockCopy(buffer.Array, buffer.Offset, nameBytes, 1, buffer.Count);
                    }

                    return new MemberSerializer
                    {
                        WriteNameExpr = Expression.Call(writer, WriteBytesMethod, Expression.Constant(nameBytes)),
                        GetValueExpr = x.Item3,
                        CustomSerializer = x.Item4
                    };
                })
                .ToArray();
        }
    }
}
