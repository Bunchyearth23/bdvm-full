using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace BDVM.Adapters;

// Call only on the state owner's thread. Enumerators and property getters run
// here; workers receive owned arrays/dictionaries and immutable scalar values.
internal static class DetachedWebSnapshot
{
    public static object Capture(object source, JsonSerializerSettings settings)
        => Copy(source, settings.ContractResolver ?? new DefaultContractResolver(), new HashSet<object>(Identity.Instance), 0)!;

    private static object? Copy(object? source, IContractResolver resolver, HashSet<object> path, int depth)
    {
        if (source == null) return null;
        var type = source.GetType();
        if (type.IsPrimitive || type.IsEnum || source is string || source is decimal || source is Guid ||
            source is DateTime || source is DateTimeOffset || source is TimeSpan) return source;
        if (depth >= 64 || !path.Add(source)) throw new InvalidOperationException("Snapshot contains a cycle or excessive nesting.");
        try
        {
            if (source is JToken token) return token.DeepClone();
            if (source is IDictionary dictionary)
            {
                var result = new Dictionary<string, object?>(StringComparer.Ordinal);
                foreach (DictionaryEntry item in dictionary)
                {
                    if (!(item.Key is string key)) throw new InvalidOperationException("Snapshot dictionary keys must be strings.");
                    result.Add(key, Copy(item.Value, resolver, path, depth + 1));
                }
                return result;
            }
            if (source is IEnumerable sequence)
            {
                var result = new List<object?>();
                foreach (var item in sequence) result.Add(Copy(item, resolver, path, depth + 1));
                return result.ToArray();
            }
            if (!(type.Namespace?.StartsWith("BDVM", StringComparison.Ordinal) == true ||
                type.IsDefined(typeof(CompilerGeneratedAttribute), false) || type == typeof(object)))
                throw new InvalidOperationException("Unsupported live snapshot type: " + type.FullName);
            var contract = resolver.ResolveContract(type) as JsonObjectContract
                ?? throw new InvalidOperationException("Unsupported snapshot contract: " + type.FullName);
            var properties = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var property in contract.Properties)
                if (!property.Ignored && property.Readable && (property.ShouldSerialize == null || property.ShouldSerialize(source)))
                    properties.Add(property.PropertyName!, Copy(property.ValueProvider!.GetValue(source), resolver, path, depth + 1));
            return properties;
        }
        finally { path.Remove(source); }
    }
    private sealed class Identity : IEqualityComparer<object>
    {
        public static readonly Identity Instance = new Identity();
        public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);
        public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
    }
}
