using System;
using System.Buffers;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
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

        public static readonly PropertyInfo InvariantCultureProperty = typeof(CultureInfo).GetRuntimeProperty(nameof(CultureInfo.InvariantCulture));
        public static readonly MemberExpression InvariantCultureExpr = Expression.Property(null, InvariantCultureProperty);

        private static readonly MethodInfo[] appendMethods = typeof(IFormatterExtensions).GetTypeInfo().DeclaredMethods
           .Where(x => x.Name == nameof(IFormatterExtensions.Append)).ToArray();
        public static readonly MethodInfo AppendStringMethodDefinition = appendMethods.First(x => x.GetParameters()[1].ParameterType == typeof(string));
        public static readonly MethodInfo AppendCharMethodDefinition = appendMethods.First(x => x.GetParameters()[1].ParameterType == typeof(char));

        public static readonly PropertyInfo IsUtf16Property = typeof(FormattingData).GetTypeInfo().DeclaredProperties.First(x => x.Name == "IsUtf16");
        public static readonly PropertyInfo IsUtf8Property = typeof(FormattingData).GetTypeInfo().DeclaredProperties.First(x => x.Name == "IsUtf8");

        public static readonly MethodInfo DisposeMethod = typeof(IDisposable).GetRuntimeMethod(nameof(IDisposable.Dispose), EmptyTypeArray);

        public static readonly Expression ThrowNotUtf8ExceptionExpr = Expression.Throw(Expression.New(
            typeof(ArgumentException).GetTypeInfo().DeclaredConstructors.First(x => x.GetParameters().Length == 1),
            Expression.Constant("The specified formatter is not for UTF-8.")));

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

        public static void WriteString<TFormatter>(TFormatter formatter, string value) where TFormatter : IFormatter
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

        public static readonly MethodInfo WriteStringMethodDefinition = GetMethod(nameof(WriteString));

        public static readonly byte[] nullUtf8 = Encoding.UTF8.GetBytes("null");
        public static readonly byte[] trueUtf8 = Encoding.UTF8.GetBytes("true");
        public static readonly byte[] falseUtf8 = Encoding.UTF8.GetBytes("false");
        public static readonly byte[] emptyObjectUtf8 = Encoding.UTF8.GetBytes("{}");

        public static readonly MemberExpression nullUtf8Expr = FieldExpr(nameof(nullUtf8));
        public static readonly MemberExpression trueUtf8Expr = FieldExpr(nameof(trueUtf8));
        public static readonly MemberExpression falseUtf8Expr = FieldExpr(nameof(falseUtf8));
        public static readonly MemberExpression emptyObjectUtf8Expr = FieldExpr(nameof(emptyObjectUtf8));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteBytes<TFormatter>(TFormatter formatter, byte[] value) where TFormatter : IFormatter
        {
            RequireBuffer(formatter, value.Length).Set(value);
            formatter.CommitBytes(value.Length);
        }

        public static readonly MethodInfo WriteBytesMethodDefinition = GetMethod(nameof(WriteBytes));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteByte<TFormatter>(TFormatter formatter, byte value) where TFormatter : IFormatter
        {
            var buffer = RequireBuffer(formatter, 1);
            buffer[0] = value;
            formatter.CommitBytes(1);
        }

        public static readonly MethodInfo WriteByteMethodDefinition = GetMethod(nameof(WriteByte));

        internal struct MemberSerializer
        {
            public MemberSerializer(Expression writeNameExpr, Expression getValueExpr)
            {
                this.WriteNameExpr = writeNameExpr;
                this.GetValueExpr = getValueExpr;
            }

            public Expression WriteNameExpr;
            public Expression GetValueExpr;
        }

        internal static MemberSerializer[] CreateMemberSerializers(Expression obj, ParameterExpression formatter)
        {
            // BufferFormatter for escaping property names.
            // Create an instance of ManagedBufferPool since a BufferFormatter never returns it's buffer.
            var bufFormatter = new BufferFormatter(30, FormattingData.InvariantUtf8, new ManagedBufferPool<byte>());
            var writeBytesMethod = WriteBytesMethodDefinition.MakeGenericMethod(formatter.Type);

            return obj.Type.GetRuntimeProperties()
                .Where(x => x.CanRead && !x.GetMethod.IsStatic && x.GetMethod.IsPublic && x.GetIndexParameters().Length == 0)
                .Select(x => Tuple.Create(x as MemberInfo, Expression.Property(obj, x)))
                .Concat(
                    obj.Type.GetRuntimeFields().Where(x => !x.IsStatic && x.IsPublic)
                    .Select(x => Tuple.Create(x as MemberInfo, Expression.Field(obj, x)))
                )
                .Where(x => !x.Item1.IsDefined(typeof(IgnoreDataMemberAttribute)))
                .Select(x =>
                {
                    var attr = x.Item1.GetCustomAttribute<DataMemberAttribute>();
                    return attr == null
                        ? Tuple.Create(-1, x.Item1.Name, x.Item2)
                        : Tuple.Create(attr.Order, attr.Name ?? x.Item1.Name, x.Item2);
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

                    return new MemberSerializer(
                        Expression.Call(writeBytesMethod, formatter, Expression.Constant(nameBytes)),
                        x.Item3
                    );
                })
                .ToArray();
        }
    }
}
