using System;
using System.Collections;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Formatting;

namespace JChopper
{
    //TODO: formatter をインスタンスフィールドにする
    //TODO: キャッシュ周りは JsonSerializer に
    public class JsonSerializerBuilder<T>
    {
        public virtual Action<T, IFormatter> GetSerializer()
        {
            var serializer = this.GetCache();
            if (serializer == null)
            {
                var prmObj = Expression.Parameter(typeof(T), "obj");
                var prmFormatter = Expression.Parameter(typeof(IFormatter), "formatter");
                serializer = Expression.Lambda<Action<T, IFormatter>>(CreateSerializer(prmObj, prmFormatter), prmObj, prmFormatter).Compile();
                this.SetCache(serializer);
            }
            return serializer;
        }

        protected virtual Action<T, IFormatter> GetCache()
        {
            return JsonSerializerCache<T>.Serializer;
        }

        protected virtual void SetCache(Action<T, IFormatter> value)
        {
            JsonSerializerCache<T>.Serializer = value;
        }

        private Expression AppendStringExpr(ParameterExpression formatter, Expression value)
        {
            Debug.Assert(value.Type == typeof(string));
            return Expression.Call(SerializationHelper.AppendStringMethod, formatter, value, SerializationHelper.DefaultFormatExpr);
        }

        private Expression AppendCharExpr(ParameterExpression formatter, Expression value)
        {
            Debug.Assert(value.Type == typeof(char));
            return Expression.Call(SerializationHelper.AppendCharMethod, formatter, value, SerializationHelper.DefaultFormatExpr);
        }

        private Expression WriteBytesExpr(ParameterExpression formatter, Expression value)
        {
            Debug.Assert(value.Type == typeof(byte[]));
            return Expression.Call(SerializationHelper.WriteBytesMethod, formatter, value);
        }

        private Expression WriteByteExpr(ParameterExpression formatter, Expression value)
        {
            Debug.Assert(value.Type == typeof(byte));
            return Expression.Call(SerializationHelper.WriteByteMethod, formatter, value);
        }

        private Expression WriteCommaExpr(ParameterExpression formatter)
        {
            return this.WriteByteExpr(formatter, Expression.Constant((byte)','));
        }

        private Expression WriteNull(ParameterExpression formatter)
        {
            return this.WriteBytesExpr(formatter, SerializationHelper.nullUtf8Expr);
        }

        private static bool IsIntegerType(Type type)
        {
            return type == typeof(byte) || type == typeof(sbyte) || type == typeof(ushort) || type == typeof(short)
                || type == typeof(uint) || type == typeof(int) || type == typeof(ulong) || type == typeof(long);
        }

        private Expression CreateCommonSerializer(Expression target, ParameterExpression formatter)
        {
            //TODO: Support Utf8String
            //TODO: CustomSerializer
            var type = target.Type;
            if (IsIntegerType(type))
                return this.SerializeInteger(target, formatter);
            if (type == typeof(bool))
                return this.SerializeBoolean(target, formatter);
            if (type == typeof(float))
                return this.SerializeFloat(target, formatter);
            if (type == typeof(double))
                return this.SerializeDouble(target, formatter);
            if (type == typeof(decimal))
                return this.SerializeDecimal(target, formatter);
            if (type == typeof(char))
                return this.SerializeChar(target, formatter);
            if (type == typeof(string))
                return this.SerializeString(target, formatter);
            if (Nullable.GetUnderlyingType(type) != null)
                return this.SerializeNullable(target, formatter);
            if (type.IsArray)
                return this.SerializeArray(target, formatter);
            if (!type.GetTypeInfo().IsDefined(typeof(AsJsonObjectAttribute))
                && typeof(IEnumerable).GetTypeInfo().IsAssignableFrom(type.GetTypeInfo()))
                return this.SerializeEnumerable(target, formatter);
            return null;
        }

        protected virtual Expression CreateSerializer(Expression target, ParameterExpression formatter)
        {
            Debug.Assert(target.Type == typeof(T));
            return Expression.Block(
                Expression.IfThen(
                    Expression.Not(
                        Expression.Property(
                            Expression.Property(formatter, typeof(IFormatter).GetRuntimeProperty(nameof(IFormatter.FormattingData))),//TODO: cache
                            SerializationHelper.IsUtf8Property
                        )
                    ),
                    SerializationHelper.ThrowNotUtf8ExceptionExpr
                ),
                this.CreateCommonSerializer(target, formatter) ?? this.SerializeObject(target, formatter)
            );
        }

        protected virtual Expression SerializeValue(Expression target, ParameterExpression formatter)
        {
            return this.CreateCommonSerializer(target, formatter) ??
                (target.Type == typeof(T)
                ? this.SerializeRecursiveObject(target, formatter)
                : this.SerializeObject(target, formatter));
        }

        private PropertyInfo serializerProperty;

        protected virtual Expression SerializeRecursiveObject(Expression target, ParameterExpression formatter)
        {
            if (this.serializerProperty == null)
                this.serializerProperty = typeof(JsonSerializerCache<T>)
                    .GetRuntimeProperty(nameof(JsonSerializerCache<T>.Serializer));

            return Expression.Invoke(Expression.Property(null, this.serializerProperty), target, formatter);
        }

        protected virtual Expression SerializeObject(Expression target, ParameterExpression formatter)
        {
            if (target.Type != typeof(T))
            {
                return Expression.Invoke(
                    Expression.Call(
                        Expression.New(this.GetType().GetGenericTypeDefinition().MakeGenericType(target.Type)),
                        nameof(GetSerializer), new Type[0]),
                    target,
                    formatter
                );
            }

            var s = SerializationHelper.CreateMemberSerializers(target, formatter);
            var body = s.Length == 0
                ? this.WriteBytesExpr(formatter, SerializationHelper.emptyObjectUtf8Expr)
                : Expression.Block(
                    s.SelectMany(x => new[] { x.WriteNameExpr, this.SerializeValue(x.GetValueExpr, formatter) })
                      .Concat(new[] { this.WriteByteExpr(formatter, Expression.Constant((byte)'}')) }));

            var local = Expression.Variable(typeof(T), "object");

            return Expression.Block(
                new[] { local },
                Expression.Assign(local, target),
                Expression.IfThenElse(
                    Expression.Equal(local, Expression.Constant(null)),
                    this.WriteNull(formatter),
                    body
                )
            );
        }

        protected virtual Expression SerializeArray(Expression target, ParameterExpression formatter)
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
                    this.WriteNull(formatter),
                    Expression.Block(
                        new[] { len },
                        this.WriteByteExpr(formatter, Expression.Constant((byte)'[')),
                        Expression.Assign(len, Expression.ArrayLength(array)),
                        Expression.IfThen(
                            Expression.GreaterThan(len, Expression.Constant(0)),
                            Expression.Block(
                                new[] { i },
                                this.SerializeValue(Expression.ArrayIndex(array, Expression.Constant(0)), formatter),
                                Expression.Assign(i, Expression.Constant(1)),
                                Expression.Loop(
                                    Expression.IfThenElse(
                                        Expression.LessThan(i, len),
                                        Expression.Block(
                                            this.WriteCommaExpr(formatter),
                                            this.SerializeValue(Expression.ArrayIndex(
                                                array, Expression.PostIncrementAssign(i)), formatter)
                                        ),
                                        Expression.Break(loopBreak)
                                    ),
                                    loopBreak
                                )
                            )
                        )
                    )
                ),
                this.WriteByteExpr(formatter, Expression.Constant((byte)']'))
            );
        }

        protected virtual Expression SerializeEnumerable(Expression target, ParameterExpression formatter)
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
                ),
                formatter);

            return Expression.Block(
                new[] { enumerable },
                Expression.Assign(enumerable, target),
                Expression.IfThenElse(
                    Expression.Equal(enumerable, Expression.Constant(null)),
                    this.WriteNull(formatter),
                    Expression.Block(
                        new[] { enumerator },
                        Expression.Assign(enumerator, Expression.Call(enumerable, getEnumerator)),
                        this.WriteByteExpr(formatter, Expression.Constant((byte)'[')),
                        Expression.TryFinally(
                            Expression.IfThen(
                                moveNext,
                                Expression.Block(
                                    writeCurrent, // Write the first element
                                    Expression.Loop(
                                        Expression.IfThenElse(
                                            moveNext,
                                            Expression.Block(
                                                this.WriteCommaExpr(formatter),
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
                        this.WriteByteExpr(formatter, Expression.Constant((byte)']'))
                    )
                )
            );
        }

        protected virtual Expression SerializeInteger(Expression target, ParameterExpression formatter)
        {
            Debug.Assert(IsIntegerType(target.Type));
            var type = target.Type;
            var appendMethod = typeof(IFormatterExtensions).GetRuntimeMethods()
                .First(x => x.Name == nameof(IFormatterExtensions.Append) && x.GetParameters()[1].ParameterType == type)
                .MakeGenericMethod(formatter.Type);
            return Expression.Call(appendMethod, formatter, target, SerializationHelper.IntegerFormatExpr);
        }

        protected virtual Expression SerializeBoolean(Expression target, ParameterExpression formatter)
        {
            Debug.Assert(target.Type == typeof(bool));
            return this.WriteBytesExpr(
                formatter,
                Expression.Condition(
                    target,
                    SerializationHelper.trueUtf8Expr,
                    SerializationHelper.falseUtf8Expr
                )
            );
        }

        private Expression FormatG(Expression target, ParameterExpression formatter, MethodInfo toString)
        {
            return this.AppendStringExpr(
                formatter,
                Expression.Call(
                    target,
                    toString,
                    Expression.Constant("G"),
                    SerializationHelper.InvariantCultureExpr
                )
            );
        }

        protected virtual Expression SerializeFloat(Expression target, ParameterExpression formatter)
        {
            Debug.Assert(target.Type == typeof(float));
            return this.FormatG(target, formatter, SerializationHelper.FloatToStringMethod);
        }

        protected virtual Expression SerializeDouble(Expression target, ParameterExpression formatter)
        {
            Debug.Assert(target.Type == typeof(double));
            return this.FormatG(target, formatter, SerializationHelper.DoubleToStringMethod);
        }

        protected virtual Expression SerializeDecimal(Expression target, ParameterExpression formatter)
        {
            Debug.Assert(target.Type == typeof(decimal));
            return this.FormatG(target, formatter, SerializationHelper.DecimalToStringMethod);
        }

        protected virtual Expression SerializeChar(Expression target, ParameterExpression formatter)
        {
            Debug.Assert(target.Type == typeof(char));
            return this.SerializeString(Expression.Call(SerializationHelper.CharToStringStaticMethod, target), formatter);
        }

        protected virtual Expression SerializeString(Expression target, ParameterExpression formatter)
        {
            Debug.Assert(target.Type == typeof(string));

            var local = Expression.Variable(typeof(string));

            return Expression.Block(
                new[] { local },
                Expression.Assign(local, target),
                Expression.Call(null, SerializationHelper.WriteStringMethod, formatter, local)
            );
        }

        protected virtual Expression SerializeNullable(Expression target, ParameterExpression formatter)
        {
            Debug.Assert(target.Type.GetGenericTypeDefinition() == typeof(Nullable<>));

            var local = Expression.Variable(target.Type, "nullable");

            return Expression.Block(
                new[] { local },
                Expression.Assign(local, target),
                Expression.IfThenElse(
                    Expression.Property(local, "HasValue"),
                    this.SerializeValue(Expression.Property(local, "Value"), formatter),
                    this.WriteNull(formatter)
                ));
        }
    }
}
