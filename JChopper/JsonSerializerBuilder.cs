using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Text.Formatting;

namespace JChopper
{
    public class JsonSerializerBuilder<T, TFormatter> where TFormatter : IFormatter
    {
        public virtual Action<T, TFormatter> GetSerializer()
        {
            var serializer = this.GetCache();
            if (serializer == null)
            {
                var prmObj = Expression.Parameter(typeof(T), "obj");
                var prmFormatter = Expression.Parameter(typeof(TFormatter), "formatter");
                serializer = Expression.Lambda<Action<T, TFormatter>>(CreateSerializer(prmObj, prmFormatter)).Compile();
                this.SetCache(serializer);
            }
            return serializer;
        }

        protected virtual Action<T, TFormatter> GetCache()
        {
            return JsonSerializerCache<T, TFormatter>.Serializer;
        }

        protected virtual void SetCache(Action<T, TFormatter> value)
        {
            JsonSerializerCache<T, TFormatter>.Serializer = value;
        }

        private static readonly MethodInfo[] appendMethods = typeof(IFormatterExtensions).GetTypeInfo().DeclaredMethods
            .Where(x => x.Name == nameof(IFormatterExtensions.Append)).ToArray();

        private static readonly MethodInfo appendStringMethodDefinition = appendMethods.First(x => x.GetParameters()[1].ParameterType == typeof(string));
        private MethodInfo appendStringMethod;
        private Expression AppendStringExpr(ParameterExpression formatter, Expression value)
        {
            if (this.appendStringMethod == null)
                this.appendStringMethod = appendStringMethodDefinition.MakeGenericMethod(typeof(TFormatter));

            return Expression.Call(this.appendStringMethod, formatter, value, InternalHelper.DefaultFormatExpr);
        }

        private static readonly MethodInfo appendCharMethodDefinition = appendMethods.First(x => x.GetParameters()[1].ParameterType == typeof(char));
        private MethodInfo appendCharMethod;
        private Expression AppendCharExpr(ParameterExpression formatter, Expression value)
        {
            if (this.appendCharMethod == null)
                this.appendCharMethod = appendCharMethodDefinition.MakeGenericMethod(typeof(TFormatter));

            return Expression.Call(this.appendCharMethod, formatter, value, InternalHelper.DefaultFormatExpr);
        }

        private static bool IsIntegerType(Type type)
        {
            return type == typeof(byte) || type == typeof(sbyte) || type == typeof(ushort) || type == typeof(short)
                || type == typeof(uint) || type == typeof(int) || type == typeof(ulong) || type == typeof(long);
        }

        private Expression CreateCommonSerializer(Expression target, ParameterExpression formatter)
        {
            var type = target.Type;
            if (IsIntegerType(type))
                return this.SerializeInteger(target, formatter);
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
            if (typeof(IEnumerable).GetTypeInfo().IsAssignableFrom(type.GetTypeInfo()))
                return this.SerializeEnumerable(target, formatter);
            return null;
        }

        protected virtual Expression CreateSerializer(Expression target, ParameterExpression formatter)
        {
            Debug.Assert(target.Type == typeof(T));
            return this.CreateCommonSerializer(target, formatter) ?? this.SerializeObject(target, formatter);
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
                this.serializerProperty = typeof(JsonSerializerCache<T, TFormatter>)
                    .GetRuntimeProperty(nameof(JsonSerializerCache<T, TFormatter>.Serializer));

            return Expression.Invoke(Expression.Property(null, this.serializerProperty), target, formatter);
        }

        private MethodInfo writeCommaMethod;
        private Expression WriteCommaExpr(ParameterExpression formatter)
        {
            if (this.writeCommaMethod == null)
                this.writeCommaMethod = InternalHelper.WriteCommaMethod.MakeGenericMethod(typeof(TFormatter));

            return Expression.Call(this.writeCommaMethod, formatter);
        }

        protected virtual Expression SerializeObject(Expression target, ParameterExpression formatter)
        {
            if (target.Type != typeof(T))
            {
                return Expression.Invoke(
                    Expression.Call(
                        Expression.New(this.GetType().GetGenericTypeDefinition().MakeGenericType(target.Type, typeof(TFormatter))),
                        nameof(GetSerializer), new Type[0]),
                    target,
                    formatter
                );
            }

            var type = typeof(T);
            var properties = type.GetRuntimeProperties()
                .Where(x => x.CanRead && !x.GetMethod.IsStatic && x.GetMethod.IsPublic);
            var fields = type.GetRuntimeFields().Where(x => !x.IsStatic && x.IsPublic);

            //TODO
        }

        protected virtual Expression SerializeArray(Expression target, ParameterExpression formatter)
        {
            Debug.Assert(target.Type.IsArray);

            var array = Expression.Variable(target.Type, "array");
            var len = Expression.Variable(typeof(int), "arrayLength");
            var i = Expression.Variable(typeof(int), "counter");
            var loopBreak = Expression.Label("array_loopbreak");

            return Expression.Block(
                new[] { array, len },
                this.AppendCharExpr(formatter, Expression.Constant('[')),
                Expression.Assign(array, target),
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
                                    this.SerializeValue(Expression.ArrayIndex(array, i), formatter)
                                ),
                                Expression.Break(loopBreak)
                            ),
                            loopBreak
                        )
                    )
                ),
                this.AppendCharExpr(formatter, Expression.Constant(']'))
            );
        }

        protected virtual Expression SerializeEnumerable(Expression target, ParameterExpression formatter)
        {
            var getEnumerator = target.Type.GetRuntimeMethod(nameof(IEnumerable.GetEnumerator), new Type[0]);
            Debug.Assert(getEnumerator != null);

            var enumerator = Expression.Variable(getEnumerator.ReturnType, "enumerator");
            var loopBreak = Expression.Label("enumerable_loopbreak");
            var disposable = Expression.Variable(typeof(IDisposable), "enumerator_disposable");

            var moveNext = Expression.Call(enumerator, getEnumerator.ReturnType.GetRuntimeMethod("MoveNext", new Type[0]));
            var writeCurrent = this.SerializeValue(
                Expression.Property(
                    enumerator,
                    getEnumerator.ReturnType.GetRuntimeProperty("Current")
                ),
                formatter);

            return Expression.Block(
                new[] { enumerator },
                Expression.Assign(enumerator, Expression.Call(target, getEnumerator)),
                this.AppendCharExpr(formatter, Expression.Constant('[')),
                Expression.TryFinally(
                    Expression.IfThen(
                        moveNext,
                        Expression.Block(
                            writeCurrent,
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
                            Expression.Call(disposable, InternalHelper.DisposeMethod)
                        )
                    )
                ),
                this.AppendCharExpr(formatter, Expression.Constant(']'))
            );
        }

        protected virtual Expression SerializeInteger(Expression target, ParameterExpression formatter)
        {
            Debug.Assert(IsIntegerType(target.Type));
            var type = target.Type;
            var appendMethod = typeof(IFormatterExtensions).GetRuntimeMethods()
                .First(x => x.Name == nameof(IFormatterExtensions.Append) && x.GetParameters()[1].ParameterType == type)
                .MakeGenericMethod(formatter.Type);
            return Expression.Call(appendMethod, formatter, target, InternalHelper.IntegerFormatExpr);
        }

        private Expression FormatG(Expression target, ParameterExpression formatter, MethodInfo toString)
        {
            return this.AppendStringExpr(
                formatter,
                Expression.Call(
                    target,
                    toString,
                    Expression.Constant("G"),
                    InternalHelper.InvariantCultureExpr
                )
            );
        }

        protected virtual Expression SerializeFloat(Expression target, ParameterExpression formatter)
        {
            Debug.Assert(target.Type == typeof(float));
            return this.FormatG(target, formatter, InternalHelper.FloatToStringMethod);
        }

        protected virtual Expression SerializeDouble(Expression target, ParameterExpression formatter)
        {
            Debug.Assert(target.Type == typeof(double));
            return this.FormatG(target, formatter, InternalHelper.DoubleToStringMethod);
        }

        protected virtual Expression SerializeDecimal(Expression target, ParameterExpression formatter)
        {
            Debug.Assert(target.Type == typeof(decimal));
            return this.FormatG(target, formatter, InternalHelper.DecimalToStringMethod);
        }

        protected virtual Expression SerializeChar(Expression target, ParameterExpression formatter)
        {
            Debug.Assert(target.Type == typeof(char));
            return this.SerializeString(Expression.Call(InternalHelper.CharToStringStaticMethod, target), formatter);
        }

        private PropertyInfo formattingDataProperty;
        private PropertyInfo FormattingDataProperty
        {
            get
            {
                return this.formattingDataProperty ??
                    (this.formattingDataProperty = typeof(TFormatter).GetRuntimeProperty(nameof(IFormatter.FormattingData)));
            }
        }

        private Expression IsUtf16(ParameterExpression formatter)
        {
            return Expression.Property(Expression.Property(formatter, this.FormattingDataProperty), InternalHelper.IsUtf16Property);
        }

        protected virtual Expression SerializeString(Expression target, ParameterExpression formatter)
        {
            Debug.Assert(target.Type == typeof(string));

            var local = Expression.Variable(typeof(string));

            return Expression.Block(
                new[] { local },
                Expression.Assign(local, target),
                Expression.Condition(
                    this.IsUtf16(formatter),
                    Expression.Call(null, InternalHelper.WriteStringToUtf16Method.MakeGenericMethod(formatter.Type), formatter, local),
                    Expression.Call(null, InternalHelper.WriteStringToUtf8Method.MakeGenericMethod(formatter.Type), formatter, local)
                )
            );
        }

        private Expression WriteNull(ParameterExpression formatter)
        {
            return Expression.Call(
                InternalHelper.WriteBytesMethod.MakeGenericMethod(formatter.Type),
                Expression.Condition(
                    this.IsUtf16(formatter),
                    InternalHelper.nullUtf16Expr,
                    InternalHelper.nullUtf8Expr
                ));
        }

        protected virtual Expression SerializeNullable(Expression target, ParameterExpression formatter)
        {
            Debug.Assert(target.Type.GetGenericTypeDefinition() == typeof(Nullable<>));

            var local = Expression.Variable(target.Type);

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
