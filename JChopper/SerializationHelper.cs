using System;
using System.Buffers;
using System.Collections;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Text;
using System.Text.Formatting;
using System.Text.Utf8;

namespace JChopper
{
    static class SerializationHelper
    {
        private static MemberExpression MemberExpr<T>(Expression<Func<T>> expr)
        {
            return ((MemberExpression)expr.Body);
        }

        private static MethodInfo GetMethod(string name)
        {
            return typeof(SerializationHelper).GetTypeInfo().GetDeclaredMethod(name);
        }

        private static readonly Format.Parsed DefaultFormat = default(Format.Parsed);
        internal static readonly MemberExpression DefaultFormatExpr = MemberExpr(() => DefaultFormat);

        private static readonly Format.Parsed IntegerFormat = Format.Parse('D');
        internal static readonly MemberExpression IntegerFormatExpr = MemberExpr(() => IntegerFormat);

        internal static readonly MethodInfo FloatToStringMethod = typeof(float).GetRuntimeMethod(nameof(float.ToString), new[] { typeof(string), typeof(IFormatProvider) });
        internal static readonly MethodInfo DoubleToStringMethod = typeof(double).GetRuntimeMethod(nameof(double.ToString), new[] { typeof(string), typeof(IFormatProvider) });
        internal static readonly MethodInfo DecimalToStringMethod = typeof(decimal).GetRuntimeMethod(nameof(double.ToString), new[] { typeof(string), typeof(IFormatProvider) });
        internal static readonly MethodInfo CharToStringStaticMethod = typeof(char).GetRuntimeMethod(nameof(char.ToString), new[] { typeof(char) });

        internal static readonly MemberExpression InvariantCultureExpr = MemberExpr(() => CultureInfo.InvariantCulture);

        private static readonly MethodInfo[] appendMethods = typeof(IFormatterExtensions).GetTypeInfo().DeclaredMethods
           .Where(x => x.Name == nameof(IFormatterExtensions.Append)).ToArray();
        internal static readonly MethodInfo AppendStringMethod = appendMethods.First(x => x.GetParameters()[1].ParameterType == typeof(string)).MakeGenericMethod(typeof(IFormatter));
        internal static readonly MethodInfo AppendCharMethod = appendMethods.First(x => x.GetParameters()[1].ParameterType == typeof(char)).MakeGenericMethod(typeof(IFormatter));

        internal static readonly PropertyInfo FormattingDataProperty = typeof(IFormatter).GetRuntimeProperty(nameof(IFormatter.FormattingData));
        internal static readonly PropertyInfo IsUtf8Property = typeof(FormattingData).GetTypeInfo().DeclaredProperties.First(x => x.Name == "IsUtf8");

        internal static readonly MethodInfo MoveNextMethod = typeof(IEnumerator).GetRuntimeMethod(nameof(IEnumerator.MoveNext), new Type[0]);
        internal static readonly MethodInfo DisposeMethod = typeof(IDisposable).GetRuntimeMethod(nameof(IDisposable.Dispose), new Type[0]);

        internal static readonly MethodInfo SerializeMethodDefinition = typeof(IJsonSerializer).GetTypeInfo().GetDeclaredMethod("Serialize");

        internal static readonly Expression ThrowNotUtf8ExceptionExpr = Expression.Throw(Expression.New(
            typeof(ArgumentException).GetTypeInfo().DeclaredConstructors.First(x => x.GetParameters().Length == 1),
            Expression.Constant("The specified formatter is not for UTF-8.")));

        private static readonly byte[] u000Utf8 = Encoding.UTF8.GetBytes("\\u000");
        private static readonly byte[] u001Utf8 = Encoding.UTF8.GetBytes("\\u001");

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void RequireBuffer(ref Span<byte> buffer, ref int bytesWritten, IFormatter formatter, int requiredBytes)
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
        private static Span<byte> RequireBuffer(IFormatter formatter, int requiredBytes)
        {
            Span<byte> buffer;
            while ((buffer = formatter.FreeBuffer).Length < requiredBytes)
                formatter.ResizeBuffer();
            return buffer;
        }

        public static void WriteString(IFormatter formatter, string value)
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
                        bytesWritten += 6;
                        buffer = buffer.Slice(6);
                        break;
                    case 1:
                        // \u001x
                        RequireBuffer(ref buffer, ref bytesWritten, formatter, 6);
                        buffer.Set(u001Utf8);
                        buffer[5] = x;
                        bytesWritten += 6;
                        buffer = buffer.Slice(6);
                        break;
                    case 2:
                        // \x
                        RequireBuffer(ref buffer, ref bytesWritten, formatter, 2);
                        buffer[0] = (byte)'\\';
                        buffer[1] = x;
                        bytesWritten += 2;
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

        internal static readonly MethodInfo WriteStringMethod = GetMethod(nameof(WriteString));

        public static void WriteUtf8String(IFormatter formatter, Utf8String value)
        {
            var buffer = RequireBuffer(formatter, 2);

            buffer[0] = (byte)'"';
            buffer = buffer.Slice(1);
            var bytesWritten = 1;

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
                    RequireBuffer(ref buffer, ref bytesWritten, formatter, slice.Length);
                    slice.CopyTo(buffer);
                    bytesWritten += slice.Length;
                    buffer = buffer.Slice(slice.Length);
                }

                switch (flag)
                {
                    case 0:
                        // \u000x
                        RequireBuffer(ref buffer, ref bytesWritten, formatter, 6);
                        buffer.Set(u000Utf8);
                        buffer[5] = x;
                        bytesWritten += 6;
                        buffer = buffer.Slice(6);
                        break;
                    case 1:
                        // \u001x
                        RequireBuffer(ref buffer, ref bytesWritten, formatter, 6);
                        buffer.Set(u001Utf8);
                        buffer[5] = x;
                        bytesWritten += 6;
                        buffer = buffer.Slice(6);
                        break;
                    case 2:
                        // \x
                        RequireBuffer(ref buffer, ref bytesWritten, formatter, 2);
                        buffer[0] = (byte)'\\';
                        buffer[1] = x;
                        bytesWritten += 2;
                        buffer = buffer.Slice(2);
                        break;
                    default:
                        throw new Exception("unreachable");
                }

                start = i + 1;
            }

            if (start < i)
            {
                var slice = value.Substring(start, i - start);
                RequireBuffer(ref buffer, ref bytesWritten, formatter, slice.Length + 1);
                slice.CopyTo(buffer);
                bytesWritten += slice.Length;
                buffer = buffer.Slice(slice.Length);
            }
            else
            {
                RequireBuffer(ref buffer, ref bytesWritten, formatter, 1);
            }

            buffer[0] = (byte)'"';
            formatter.CommitBytes(bytesWritten + 1);
        }

        internal static readonly MethodInfo WriteUtf8StringMethod = GetMethod(nameof(WriteUtf8String));

        private static readonly byte[] nullUtf8 = Encoding.UTF8.GetBytes("null");
        private static readonly byte[] trueUtf8 = Encoding.UTF8.GetBytes("true");
        private static readonly byte[] falseUtf8 = Encoding.UTF8.GetBytes("false");
        private static readonly byte[] emptyObjectUtf8 = Encoding.UTF8.GetBytes("{}");

        internal static readonly MemberExpression nullUtf8Expr = MemberExpr(() => nullUtf8);
        internal static readonly MemberExpression trueUtf8Expr = MemberExpr(() => trueUtf8);
        internal static readonly MemberExpression falseUtf8Expr = MemberExpr(() => falseUtf8);
        internal static readonly MemberExpression emptyObjectUtf8Expr = MemberExpr(() => emptyObjectUtf8);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteBytes(IFormatter formatter, byte[] value)
        {
            RequireBuffer(formatter, value.Length).Set(value);
            formatter.CommitBytes(value.Length);
        }

        internal static readonly MethodInfo WriteBytesMethod = GetMethod(nameof(WriteBytes));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteByte(IFormatter formatter, byte value)
        {
            var buffer = RequireBuffer(formatter, 1);
            buffer[0] = value;
            formatter.CommitBytes(1);
        }

        internal static readonly MethodInfo WriteByteMethod = GetMethod(nameof(WriteByte));

        internal class MemberSerializer
        {
            public Expression WriteNameExpr;
            public Expression GetValueExpr;
            public Type CustomSerializer;
        }

        internal static MemberSerializer[] CreateMemberSerializers(Expression obj, ParameterExpression formatter)
        {
            // BufferFormatter for escaping property names.
            // Create an instance of ManagedBufferPool since a BufferFormatter never returns it's buffer.
            var bufFormatter = new BufferFormatter(30, FormattingData.InvariantUtf8, new ManagedBufferPool<byte>());

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
                    bufFormatter.Clear();
                    WriteString(bufFormatter, x.Item2);

                    // nameBytes
                    // { '{' or ',', ... escaped string ..., ':' }
                    var commitedByteCount = bufFormatter.CommitedByteCount;
                    var nameBytes = new byte[commitedByteCount + 2];
                    nameBytes[0] = i == 0 ? (byte)'{' : (byte)',';
                    nameBytes[commitedByteCount + 1] = (byte)':';
                    Buffer.BlockCopy(bufFormatter.Buffer, 0, nameBytes, 1, commitedByteCount);

                    return new MemberSerializer
                    {
                        WriteNameExpr = Expression.Call(WriteBytesMethod, formatter, Expression.Constant(nameBytes)),
                        GetValueExpr = x.Item3,
                        CustomSerializer = x.Item4
                    };
                })
                .ToArray();
        }
    }
}
