using CSharp_Library.Utility;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace AutoBuffer {
    public class EntityMapper {
        /// <summary>
        /// List all type members that will be mapped to/from BsonDocument
        /// </summary>
        public List<MemberMapper> Members { get; set; }

        /// <summary>
        /// Indicate which Type this entity mapper is
        /// </summary>
        public Type ForType { get; set; }

        public MethodInfo Serializer { get; set; }
        public MethodInfo Deserializer { get; set; }

        public bool CustomSerializers() {
            return (Serializer != null && Deserializer != null);
        }
    }

    public class MemberMapper {

        public string Name { get; set; }
        public TypeCache Cache { get; set; }
        public Type DataType { get; set; }
        public bool SkipType { get; set; }
        public bool SkipIsNull { get; set; }
        
        /// <summary>
        /// Delegate method to get value from entity instance
        /// </summary>
        public GenericGetter Getter { get; set; }

        /// <summary>
        /// Delegate method to set value to entity instance
        /// </summary>
        public GenericSetter Setter { get; set; }
    }

    [Flags]
    public enum TypeCacheFlags {
        None = 0,
        IsNullable = 1 << 1,
        IsEnum = 1 << 2,
        IsList = 1 << 3,
        HasChildTypes = 1 << 4,
        IsGeneric = 1 << 5,
        IsClass = 1 << 6
    }

    public static class TypeCacheFlagsExt {
        public static bool IsSet(this TypeCacheFlags source, TypeCacheFlags flag) {
            return (source & flag) > 0;
        }

        public static TypeCacheFlags Set(this TypeCacheFlags source, TypeCacheFlags flag) {
            return source | flag;
        }

        public static TypeCacheFlags Unset(this TypeCacheFlags source, TypeCacheFlags flag) {
            return source & (~flag);
        }

        public static TypeCacheFlags Toggle(this TypeCacheFlags source, TypeCacheFlags flag, bool value) {
            if (value)
                return source.Set(flag);
            return source.Unset(flag);
        }
    }

    public struct TypeCache {
        TypeCacheFlags Flags;

        public static TypeCache Builder(Type type, ICollection<Type> allTypes) {
            TypeCache result = default(TypeCache);
            result.IsNullable = Reflection.IsNullable(type);
            result.IsList = Reflection.IsList(type);
            result.IsEnum = type.IsEnum;
            result.IsGeneric = type.IsGenericType;
            result.IsClass = type.IsClass;

            if (type.IsSealed)
                return result;

            bool hasChild = false;
            foreach (Type storedType in allTypes) {
                if (storedType == type)
                    continue;
                if (storedType.IsSubclassOf(type)) {
                    hasChild = true;
                    break;
                }
            }

            result.HasChildTypes = hasChild;
            return result;
        }

        public bool IsNullable {
            get { return Flags.IsSet(TypeCacheFlags.IsNullable); }
            set { Flags = Flags.Toggle(TypeCacheFlags.IsNullable, value); }
        }

        public bool IsEnum {
            get { return Flags.IsSet(TypeCacheFlags.IsEnum); }
            set { Flags = Flags.Toggle(TypeCacheFlags.IsEnum, value); }
        }

        public bool IsList {
            get { return Flags.IsSet(TypeCacheFlags.IsList); }
            set { Flags = Flags.Toggle(TypeCacheFlags.IsList, value); }
        }

        public bool HasChildTypes {
            get { return Flags.IsSet(TypeCacheFlags.HasChildTypes); }
            set { Flags = Flags.Toggle(TypeCacheFlags.HasChildTypes, value); }
        }

        public bool IsGeneric {
            get { return Flags.IsSet(TypeCacheFlags.IsGeneric); }
            set { Flags = Flags.Toggle(TypeCacheFlags.IsGeneric, value); }
        }

        public bool IsClass {
            get { return Flags.IsSet(TypeCacheFlags.IsClass); }
            set { Flags = Flags.Toggle(TypeCacheFlags.IsClass, value); }
        }

        public bool CanBeNull() {
            TypeCacheFlags combined = TypeCacheFlags.IsClass | TypeCacheFlags.IsNullable;
            return Flags.IsSet(combined);
        }
    }

}
