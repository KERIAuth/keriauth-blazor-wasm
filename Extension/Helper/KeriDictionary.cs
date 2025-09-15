using System.Collections.Specialized;

namespace Extension.Helper {
    /// <summary>
    /// Type alias for KERI/ACDC data structures that require preserved field ordering
    /// and support for nested dictionaries, primitives, and lists.
    /// Use this instead of Dictionary&lt;string, object&gt; for signify-ts interop.
    /// </summary>
    public class KeriDictionary : RecursiveDictionary {
        public KeriDictionary() : base() { }
        
        public KeriDictionary(int capacity) : base(capacity) { }
        
        public KeriDictionary(IDictionary<string, RecursiveValue> dictionary) : base(dictionary) { }
        
        /// <summary>
        /// Creates a KeriDictionary from an OrderedDictionary (preserves order)
        /// </summary>
        public static KeriDictionary FromOrderedDictionary(OrderedDictionary source) {
            var result = new KeriDictionary();
            foreach (var key in source.Keys) {
                if (key is string strKey && source[key] != null) {
                    result[strKey] = RecursiveValue.FromObject(source[key]);
                }
            }
            return result;
        }
        
        /// <summary>
        /// Converts to OrderedDictionary for compatibility with existing code
        /// </summary>
        public OrderedDictionary ToOrderedDictionary() {
            var result = new OrderedDictionary();
            foreach (var kvp in this) {
                result[kvp.Key] = kvp.Value.ToObject();
            }
            return result;
        }
    }
    
    /// <summary>
    /// Extension methods for easier migration from Dictionary&lt;string, object&gt;
    /// </summary>
    public static class KeriDictionaryExtensions {
        /// <summary>
        /// Converts a Dictionary&lt;string, object&gt; to KeriDictionary
        /// </summary>
        public static KeriDictionary ToKeriDictionary(this Dictionary<string, object> source) {
            return (KeriDictionary)RecursiveDictionary.FromObjectDictionary(source);
        }
        
        /// <summary>
        /// Converts an OrderedDictionary to KeriDictionary
        /// </summary>
        public static KeriDictionary ToKeriDictionary(this OrderedDictionary source) {
            return KeriDictionary.FromOrderedDictionary(source);
        }
    }
}