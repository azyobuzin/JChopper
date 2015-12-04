# JChopper
A simple JSON serializer, which:

* generates [Utf8String](https://github.com/dotnet/corefxlab) directly
* will be compiled to IL dynamically with Expression Trees

# Serializing
```csharp
Utf8String result =
JsonSerializer.Default.Serialize(obj);
```

# Deserializing
not yet 🍣
