using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Formatting;
using System.Text.Utf8;

namespace JChopper
{
    public class JsonSerializerBuilder<T>
    {
        public JsonSerializerBuilder(IJsonSerializer parent)
        {
            this.Parent = parent;
            this.parentExpr = Expression.Constant(parent);
        }

        public IJsonSerializer Parent { get; }
        private readonly Expression parentExpr;

        protected ParameterExpression FormatterParameter { get; } = Expression.Parameter(typeof(IFormatter), "formatter");

        private readonly Dictionary<Type, ConstantExpression> customSerializers = new Dictionary<Type, ConstantExpression>();

        public virtual Action<T, IFormatter> CreateSerializer()
        {
            var prmObj = Expression.Parameter(typeof(T), "obj");
            return Expression.Lambda<Action<T, IFormatter>>(
                CreateSerializerExpression(prmObj),
                prmObj,
                this.FormatterParameter
            ).Compile();
        }

        private Expression AppendStringExpr(Expression value)
        {
            Debug.Assert(value.Type == typeof(string));
            return Expression.Call(SerializationHelper.AppendStringMethod, this.FormatterParameter, value, SerializationHelper.DefaultFormatExpr);
        }

        private Expression AppendCharExpr(Expression value)
        {
            Debug.Assert(value.Type == typeof(char));
            return Expression.Call(SerializationHelper.AppendCharMethod, this.FormatterParameter, value, SerializationHelper.DefaultFormatExpr);
        }

        private Expression WriteBytesExpr(Expression value)
        {
            Debug.Assert(value.Type == typeof(byte[]));
            return Expression.Call(SerializationHelper.WriteBytesMethod, this.FormatterParameter, value);
        }

        private Expression WriteByteExpr(Expression value)
        {
            Debug.Assert(value.Type == typeof(byte));
            return Expression.Call(SerializationHelper.WriteByteMethod, this.FormatterParameter, value);
        }

        private Expression WriteCommaExpr()
        {
            return this.WriteByteExpr(Expression.Constant((byte)','));
        }

        private Expression WriteNull()
        {
            return this.WriteBytesExpr(SerializationHelper.nullUtf8Expr);
        }

        private static bool IsIntegerType(Type type)
        {
            return type == typeof(byte) || type == typeof(sbyte) || type == typeof(ushort) || type == typeof(short)
                || type == typeof(uint) || type == typeof(int) || type == typeof(ulong) || type == typeof(long);
        }

        private Expression CreateCommonSerializer(Expression target)
        {
            var type = target.Type;
            if (IsIntegerType(type))
                return this.SerializeInteger(target);
            if (type == typeof(bool))
                return this.SerializeBoolean(target);
            if (type == typeof(float))
                return this.SerializeFloat(target);
            if (type == typeof(double))
                return this.SerializeDouble(target);
            if (type == typeof(decimal))
                return this.SerializeDecimal(target);
            if (type == typeof(char))
                return this.SerializeChar(target);
            if (type == typeof(string))
                return this.SerializeString(target);
            if (type == typeof(Utf8String))
                return this.SerializeUtf8String(target);
            if (Nullable.GetUnderlyingType(type) != null)
                return this.SerializeNullable(target);
            if (type.IsArray)
                return this.SerializeArray(target);
            if (!type.GetTypeInfo().IsDefined(typeof(AsJsonObjectAttribute))
                && typeof(IEnumerable).GetTypeInfo().IsAssignableFrom(type.GetTypeInfo()))
                return this.SerializeEnumerable(target);
            return null;
        }

        protected virtual Expression CreateSerializerExpression(Expression target)
        {
            Debug.Assert(target.Type == typeof(T));
            return Expression.Block(
                Expression.IfThen(
                    Expression.Not(
                        Expression.Property(
                            Expression.Property(this.FormatterParameter, SerializationHelper.FormattingDataProperty),
                            SerializationHelper.IsUtf8Property
                        )
                    ),
                    SerializationHelper.ThrowNotUtf8ExceptionExpr
                ),
                this.CreateCommonSerializer(target) ?? this.SerializeObject(target)
            );
        }

        protected virtual Expression SerializeValue(Expression target)
        {
            return this.CreateCommonSerializer(target) ?? this.SerializeOtherObject(target);
        }

        protected virtual Expression SerializeOtherObject(Expression target)
        {
            return Expression.Call(
                this.parentExpr,
                SerializationHelper.SerializeMethodDefinition.MakeGenericMethod(target.Type),
                target,
                this.FormatterParameter);
        }

        protected virtual Expression SerializeObject(Expression target)
        {
            Debug.Assert(target.Type == typeof(T));

            var s = SerializationHelper.CreateMemberSerializers(target, this.FormatterParameter);
            var body = s.Length == 0
                ? this.WriteBytesExpr(SerializationHelper.emptyObjectUtf8Expr)
                : Expression.Block(
                    s.SelectMany(x =>
                    {
                        Expression serializeExpr;
                        if (x.CustomSerializer == null)
                        {
                            serializeExpr = this.SerializeValue(x.GetValueExpr);
                        }
                        else
                        {
                            ConstantExpression cs;
                            if (!this.customSerializers.TryGetValue(x.CustomSerializer, out cs))
                            {
                                cs = Expression.Constant(Activator.CreateInstance(x.CustomSerializer));
                                this.customSerializers.Add(x.CustomSerializer, cs);
                            }
                            serializeExpr = Expression.Call(cs, "Serialize", null, x.GetValueExpr, this.FormatterParameter);
                        }
                        return new[] { x.WriteNameExpr, serializeExpr };
                    })
                    .Concat(new[] { this.WriteByteExpr(Expression.Constant((byte)'}')) }));

            var local = Expression.Variable(typeof(T), "object");

            return Expression.Block(
                new[] { local },
                Expression.Assign(local, target),
                Expression.IfThenElse(
                    Expression.Equal(local, Expression.Constant(null)),
                    this.WriteNull(),
                    body
                )
            );
        }

        protected virtual Expression SerializeArray(Expression target)
        {
            Debug.Assert(target.Type.IsArray);

            var array = Expression.Variable(target.Type, "array");
            var len = Expression.Variable(typeof(int), "arrayLength");
            var i = Expression.Variable(typeof(int), "counter");
            var loopBreak = Expression.Label("array_loopbreak");

            return Expression.Block(
                new[] { array },
                Expression.Assign(array, target),
                Expression.IfThenElse(
                    Expression.Equal(array, Expression.Constant(null)),
                    this.WriteNull(),
                    Expression.Block(
                        new[] { len },
                        this.WriteByteExpr(Expression.Constant((byte)'[')),
                        Expression.Assign(len, Expression.ArrayLength(array)),
                        Expression.IfThen(
                            Expression.GreaterThan(len, Expression.Constant(0)),
                            Expression.Block(
                                new[] { i },
                                this.SerializeValue(Expression.ArrayIndex(array, Expression.Constant(0))),
                                Expression.Assign(i, Expression.Constant(1)),
                                Expression.Loop(
                                    Expression.IfThenElse(
                                        Expression.LessThan(i, len),
                                        Expression.Block(
                                            this.WriteCommaExpr(),
                                            this.SerializeValue(Expression.ArrayIndex(
                                                array, Expression.PostIncrementAssign(i)))
                                        ),
                                        Expression.Break(loopBreak)
                                    ),
                                    loopBreak
                                )
                            )
                        )
                    )
                ),
                this.WriteByteExpr(Expression.Constant((byte)']'))
            );
        }

        protected virtual Expression SerializeEnumerable(Expression target)
        {
            var getEnumerator = target.Type.GetRuntimeMethod(nameof(IEnumerable.GetEnumerator), new Type[0]);
            Debug.Assert(getEnumerator != null);

            var enumerable = Expression.Variable(target.Type, "enumerable");
            var enumerator = Expression.Variable(getEnumerator.ReturnType, "enumerator");
            var loopBreak = Expression.Label("enumerable_loopbreak");
            var disposable = Expression.Variable(typeof(IDisposable), "enumerator_disposable");

            var moveNext = Expression.Call(enumerator,
                getEnumerator.ReturnType.GetRuntimeMethod("MoveNext", new Type[0]) // Duck typing
                ?? SerializationHelper.MoveNextMethod); // IEnumerator
            var writeCurrent = this.SerializeValue(
                Expression.Property(
                    enumerator,
                    getEnumerator.ReturnType.GetRuntimeProperty("Current")
                ));

            return Expression.Block(
                new[] { enumerable },
                Expression.Assign(enumerable, target),
                Expression.IfThenElse(
                    Expression.Equal(enumerable, Expression.Constant(null)),
                    this.WriteNull(),
                    Expression.Block(
                        new[] { enumerator },
                        Expression.Assign(enumerator, Expression.Call(enumerable, getEnumerator)),
                        this.WriteByteExpr(Expression.Constant((byte)'[')),
                        Expression.TryFinally(
                            Expression.IfThen(
                                moveNext,
                                Expression.Block(
                                    writeCurrent, // Write the first element
                                    Expression.Loop(
                                        Expression.IfThenElse(
                                            moveNext,
                                            Expression.Block(
                                                this.WriteCommaExpr(),
                                                writeCurrent
                                            ),
                                            Expression.Break(loopBreak)
                                        ),
                                        loopBreak
                                    )
                                )
                            ),
                            Expression.Block(
                                new[] { disposable },
                                Expression.Assign(disposable, Expression.TypeAs(enumerator, typeof(IDisposable))),
                                Expression.IfThen(
                                    Expression.NotEqual(disposable, Expression.Constant(null)),
                                    Expression.Call(disposable, SerializationHelper.DisposeMethod)
                                )
                            )
                        ),
                        this.WriteByteExpr(Expression.Constant((byte)']'))
                    )
                )
            );
        }

        protected virtual Expression SerializeInteger(Expression target)
        {
            Debug.Assert(IsIntegerType(target.Type));
            var type = target.Type;
            var appendMethod = typeof(IFormatterExtensions).GetRuntimeMethods()
                .First(x => x.Name == nameof(IFormatterExtensions.Append) && x.GetParameters()[1].ParameterType == type)
                .MakeGenericMethod(this.FormatterParameter.Type);
            return Expression.Call(appendMethod, this.FormatterParameter, target, SerializationHelper.IntegerFormatExpr);
        }

        protected virtual Expression SerializeBoolean(Expression target)
        {
            Debug.Assert(target.Type == typeof(bool));
            return this.WriteBytesExpr(
                Expression.Condition(
                    target,
                    SerializationHelper.trueUtf8Expr,
                    SerializationHelper.falseUtf8Expr
                )
            );
        }

        private Expression FormatG(Expression target, MethodInfo toString)
        {
            return this.AppendStringExpr(
                Expression.Call(
                    target,
                    toString,
                    Expression.Constant("G"),
                    SerializationHelper.InvariantCultureExpr
                )
            );
        }

        protected virtual Expression SerializeFloat(Expression target)
        {
            Debug.Assert(target.Type == typeof(float));
            return this.FormatG(target, SerializationHelper.FloatToStringMethod);
        }

        protected virtual Expression SerializeDouble(Expression target)
        {
            Debug.Assert(target.Type == typeof(double));
            return this.FormatG(target, SerializationHelper.DoubleToStringMethod);
        }

        protected virtual Expression SerializeDecimal(Expression target)
        {
            Debug.Assert(target.Type == typeof(decimal));
            return this.FormatG(target, SerializationHelper.DecimalToStringMethod);
        }

        protected virtual Expression SerializeChar(Expression target)
        {
            Debug.Assert(target.Type == typeof(char));
            return this.SerializeString(Expression.Call(SerializationHelper.CharToStringStaticMethod, target));
        }

        protected virtual Expression SerializeString(Expression target)
        {
            Debug.Assert(target.Type == typeof(string));
            return Expression.Call(null, SerializationHelper.WriteStringMethod, this.FormatterParameter, target);
        }

        protected virtual Expression SerializeUtf8String(Expression target)
        {
            Debug.Assert(target.Type == typeof(Utf8String));
            return Expression.Call(null, SerializationHelper.WriteUtf8StringMethod, this.FormatterParameter, target);
        }

        protected virtual Expression SerializeNullable(Expression target)
        {
            Debug.Assert(target.Type.GetGenericTypeDefinition() == typeof(Nullable<>));

            var local = Expression.Variable(target.Type, "nullable");

            return Expression.Block(
                new[] { local },
                Expression.Assign(local, target),
                Expression.IfThenElse(
                    Expression.Property(local, "HasValue"),
                    this.SerializeValue(Expression.Property(local, "Value")),
                    this.WriteNull()
                ));
        }
    }
}
