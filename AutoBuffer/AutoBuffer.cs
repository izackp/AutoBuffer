using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using CSharp_Library.Extensions;
using CSharp_Library.Utility;

//DateTime is stripped of any locality
//Use of generics allow your header id to go beyond 8191 (up to 65535) since there are no bits used for flags
//Header ID 0 is used for null
//Does Not Support Nested Generics: https://stackoverflow.com/questions/38702166/get-number-of-generic-type-parameters
/*
 * public class A<T>
 * {
 *   public class Nested<V>
 *   {
 * 
 *   }
 * }
 */

namespace AutoBuffer {
    public class Ignore : Attribute {

    }

    public class Serialize : Attribute {

    }

    public class Deserialize : Attribute {

    }

    public class Index : Attribute {
        public int Value;
        public Index(int i) {
            Value = i;
        }
    }

    public class SkipMetaData : Attribute {
        public bool Type = false;
        public bool IsNull = false;
        public SkipMetaData(bool skipType = false, bool skipIsNull = false) {
            Type = skipType;
            IsNull = skipIsNull;
        }
    }

    public class AutoBufferType : Attribute {
        public int HeaderId;
        public AutoBufferType(int HeaderId) {
            this.HeaderId = HeaderId;
        }
    }
    
    public partial class Serializer {

        public Dictionary<Type, Type> TypeMapper = new Dictionary<Type, Type>();
        public bool IncludeNonPublicProperties = false;
        public bool IncludeFields = false;
        
        KeyedCacheFactory<Type, EntityMapper> _entities = new KeyedCacheFactory<Type, EntityMapper>();
        KeyedCacheFactory<Type, TypeCache> _typeCacheFactory = new KeyedCacheFactory<Type, TypeCache>();

        //
        public TypeCache GetTypeCache(Type type) {
            TypeCache ret;
            if (_typeCacheFactory.GetInstance(type, out ret))
                return ret;
            if (type == null)
                return ret;
            if (type.IsGenericType)
                type = type.GetGenericTypeDefinition();
            else if (type.IsEnum)
                type = Enum.GetUnderlyingType(type);
            return _typeCacheFactory.GetOrBuildInstance(type);
        }

        public EntityMapper GetEntityMapper(Type type, bool build = true) {
            EntityMapper mapper = GetEntityMapper(type, build, IncludeFields, IncludeNonPublicProperties, false);
            return mapper;
        }

        public EntityMapper GetEntityMapper(Type type, bool build, bool includeFields, bool includeNonPublic, bool ignoreSetter) {
            if (build)
                return _entities.GetOrBuildInstance(type, (Type type2) => {//TODO: How expensive is using a dynamic callback? write down answer after lookup
                    return BuildEntityMapper(type2, includeFields, includeNonPublic, ignoreSetter);
                });

            return _entities.GetInstance(type);
        }

        protected EntityMapper BuildEntityMapper2(Type type) {
            return BuildEntityMapper(type);
        }

        protected EntityMapper BuildEntityMapper(Type type, bool includeFields = false, bool includeNonPublic = false, bool ignoreSetter = false) {
            var mapper = new EntityMapper {
                Members = new List<MemberMapper>(),
                ForType = type
            };

            MethodInfo[] listMethods = type.GetMethods();
            mapper.Serializer = Reflection.MethodWithAttribute<Serialize>(listMethods);
            mapper.Deserializer = Reflection.MethodWithAttribute<Deserialize>(listMethods);

            if (mapper.Serializer != null && mapper.Deserializer != null)
                return mapper;

            IEnumerable<MemberInfoWithMeta> members = null;
            bool isIndexed = IsTypeIndexed(type);
            if (isIndexed)
                members = GetIndexedTypeMembers(type);
            else
                members = GetTypeMembers(type, includeFields, includeNonPublic);

            foreach (MemberInfoWithMeta memberWithMeta in members) {
                MemberInfo memberInfo = memberWithMeta.Info;
                string name = memberInfo.Name;
                GenericGetter getter = null;
                GenericSetter setter = null;

                try {
                    getter = Reflection.CreateGenericGetter(type, memberInfo);
                    setter = Reflection.CreateGenericSetter(type, memberInfo);
                }
                catch (Exception ex) {
                    throw new Exception("Could not generate getter and setter for type: " + type.ToString() + " member: " + name);
                }

                if (ignoreSetter == false)
                    if (getter == null || setter == null)
                        continue; //They're null when they don't exist

                Type dataType = memberInfo is PropertyInfo ?
                    (memberInfo as PropertyInfo).PropertyType :
                    (memberInfo as FieldInfo).FieldType;

                var member = new MemberMapper {
                    Name = name,
                    DataType = dataType,
                    Getter = getter,
                    Setter = setter,
                    SkipIsNull = memberWithMeta.SkipIsNull,
                    SkipType = memberWithMeta.SkipType
                };
                mapper.Members.Add(member);
            }

            return mapper;
        }

        bool IsTypeIndexed(Type type) {
            var flags = (BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
            IEnumerable<PropertyInfo> properties = type.GetProperties(flags).Where(x => x.CanRead);
            foreach (PropertyInfo info in properties) {
                if (info.IsDefined(typeof(Index), true))
                    return true;
            }

            IEnumerable<FieldInfo> fields = type.GetFields(flags).Where(x => !x.Name.EndsWith("k__BackingField") && x.IsStatic == false);
            foreach (FieldInfo info in fields) {
                if (info.IsDefined(typeof(Index), true))
                    return true;
            }

            return false;
        }

        IEnumerable<MemberInfoWithMeta> GetIndexedTypeMembers(Type type) {
            List<KeyValuePair<int, MemberInfoWithMeta>> indexedMemberInfo = new List<KeyValuePair<int, MemberInfoWithMeta>>(20);

            var flags = (BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
            MemberInfo[] properties = type.GetProperties(flags).Where(x => x.CanRead).ToArray();
            MemberInfo[] fields = type.GetFields(flags).Where(x => !x.Name.EndsWith("k__BackingField") && x.IsStatic == false).ToArray();
            MemberInfo[] allValues = ArrayExt.Combine(properties, fields);
            foreach (MemberInfo property in allValues) {
                object[] attributes = property.GetCustomAttributes(true);
                int index = -1;
                bool skipType = false;
                bool skipIsNull = false;
                foreach (Attribute eachAttr in attributes) {
                    if (eachAttr is Ignore) {
                        index = -1;
                        break;
                    }

                    Index indexAttr = eachAttr as Index;
                    if (indexAttr != null) {
                        index = indexAttr.Value;
                        continue;
                    }

                    SkipMetaData skipAttr = eachAttr as SkipMetaData;
                    if (skipAttr != null) {
                        skipType = skipAttr.Type;
                        skipIsNull = skipAttr.IsNull;
                    }
                }

                if (index != -1) {
                    MemberInfoWithMeta memberInfo = new MemberInfoWithMeta();
                    memberInfo.Info = property;
                    memberInfo.SkipIsNull = skipIsNull;
                    memberInfo.SkipType = skipType;
                    indexedMemberInfo.Add(new KeyValuePair<int, MemberInfoWithMeta>(index, memberInfo));
                }
            }

            indexedMemberInfo.Sort((x, y) => x.Key.CompareTo(y.Key));
            var members = new List<MemberInfoWithMeta>(indexedMemberInfo.Count);
            foreach (KeyValuePair<int, MemberInfoWithMeta> kvp in indexedMemberInfo) {
                members.Add(kvp.Value);
            }

            return members;
        }

        IEnumerable<MemberInfoWithMeta> GetTypeMembers(Type type, bool includeFields = false, bool includeNonPublic = false) {
            var members = new List<MemberInfoWithMeta>();

            var flags = (BindingFlags.Public | BindingFlags.Instance);
            if (IncludeNonPublicProperties)
                flags |= BindingFlags.NonPublic;

            MemberInfo[] properties = type.GetProperties(flags).Where(x => x.CanRead).ToArray();
            MemberInfo[] fields = type.GetFields(flags).Where(x => !x.Name.EndsWith("k__BackingField") && x.IsStatic == false).ToArray();
            MemberInfo[] allValues = ArrayExt.Combine(properties, fields);

            foreach (MemberInfo property in allValues) {
                object[] attributes = property.GetCustomAttributes(true);
                MemberInfoWithMeta memberInfo = new MemberInfoWithMeta();
                memberInfo.Info = property;
                bool ignore = false;
                foreach (Attribute eachAttr in attributes) {
                    if (eachAttr is Ignore) {
                        ignore = true;
                        break;
                    }
                    SkipMetaData skipAttr = eachAttr as SkipMetaData;
                    if (skipAttr == null)
                        continue;

                    memberInfo.SkipType = skipAttr.Type;
                    memberInfo.SkipIsNull = skipAttr.IsNull;
                }
                if (ignore)
                    continue;

                members.Add(memberInfo);
            }

            return members;
        }

        //TODO: Move somewhere more appropiate
        static class Timestamp {
            internal static readonly DateTime UnixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            internal const long BclSecondsAtUnixEpoch = 62135596800;
            internal const long UnixSecondsAtBclMaxValue = 253402300799;
            internal const long UnixSecondsAtBclMinValue = -BclSecondsAtUnixEpoch;
            internal const int MaxNanos = Duration.NanosecondsPerSecond - 1;

            internal static bool IsNormalized(long seconds, int nanoseconds) {
                return nanoseconds >= 0 &&
                    nanoseconds <= MaxNanos &&
                    seconds >= UnixSecondsAtBclMinValue &&
                    seconds <= UnixSecondsAtBclMaxValue;
            }
        }

        static class Duration {
            public const int NanosecondsPerSecond = 1000000000;
            public const int NanosecondsPerTick = 100;
            public const long MaxSeconds = 315576000000L;
            public const long MinSeconds = -315576000000L;
            internal const int MaxNanoseconds = NanosecondsPerSecond - 1;
            internal const int MinNanoseconds = -NanosecondsPerSecond + 1;

            internal static bool IsNormalized(long seconds, int nanoseconds) {
                // Simple boundaries
                if (seconds < MinSeconds || seconds > MaxSeconds ||
                    nanoseconds < MinNanoseconds || nanoseconds > MaxNanoseconds) {
                    return false;
                }
                // We only have a problem is one is strictly negative and the other is
                // strictly positive.
                return Math.Sign(seconds) * Math.Sign(nanoseconds) != -1;
            }
        }
    }
    
    public partial class Serializer {

        readonly public int cMaxHeaderValue = 8191;
        readonly public int cMaxHeaderUnusedValue = 8167;

        Dictionary<int, Type> _headerTypeMap = new Dictionary<int, Type>() {
            { 0, null },
            { 8191, typeof(String) },
            { 8190, typeof(Int16) },
            { 8189, typeof(Int32) },
            { 8188, typeof(Int64) },
            { 8187, typeof(UInt16) },
            { 8186, typeof(UInt32) },
            { 8185, typeof(UInt64) },
            { 8184, typeof(Boolean) },
            { 8183, typeof(char) },
            { 8182, typeof(byte) },
            { 8181, typeof(sbyte) },
            { 8180, typeof(float) },
            { 8179, typeof(Double) },
            { 8177, typeof(Decimal) },
            { 8176, typeof(Guid) },
            { 8175, typeof(DateTime) },
            { 8174, typeof(TimeSpan) },
            { 8173, typeof(IEnumerable<>) },
            { 8172, typeof(Dictionary<,>) },
            { 8171, typeof(Nullable<>) },
            { 8170, typeof(KeyValuePair<,>) },
            { 8169, typeof(KeyValuePairWritable<,>) },
            { 8168, typeof(List<>) },
            { 8167, typeof(IEnumerable) },
            { 8166, typeof(object) },
        };

        Dictionary<Type, int> _typeHeaderMap;

        public Serializer(Assembly assemblyWithModels) {
            if (assemblyWithModels == null) {
                assemblyWithModels = Assembly.GetExecutingAssembly();
            }
            _entities.Builder = BuildEntityMapper2;
            _typeCacheFactory.Builder = (Type type) => {
                TypeCache result = TypeCache.Builder(type, _typeHeaderMap.Keys);
                return result;
            };

            //Add inverse dictionary
            _typeHeaderMap = new Dictionary<Type, int>(_headerTypeMap.Count);
            foreach (KeyValuePair<int, Type> kvp in _headerTypeMap) {
                if (kvp.Value == null)
                    continue;
                _typeHeaderMap.Add(kvp.Value, kvp.Key);
            }

            AddTypesFromAttributes(assemblyWithModels);
            BuildTypeCache();
        }

        void AddTypesFromAttributes(Assembly assemblyWithModels) {
            foreach (Type type in assemblyWithModels.GetTypes()) {
                object[] attributes = type.GetCustomAttributes(typeof(AutoBufferType), false);
                if (attributes.Length == 0)
                    continue;
                AutoBufferType headerInfo = attributes[0] as AutoBufferType;
                if (headerInfo.HeaderId > 0xFFFF)
                    throw new Exception("Header out of bounds");
                AddMapping(headerInfo.HeaderId, type);
            }
        }

        public void BuildTypeCache() {
            foreach (Type type in _typeHeaderMap.Keys) {
                _typeCacheFactory.GetOrBuildInstance(type);
            }
        }

        public void AddMappings(Dictionary<int, Type> mappings) {
            foreach(KeyValuePair<int, Type> item in mappings) {
                AddMapping(item.Key, item.Value);
            }
        }

        public void AddMapping(int headerId, Type type) {
            if (headerId > 0xFFFF)
                throw new Exception("Header out of bounds");

            if (_headerTypeMap.ContainsKey(headerId))
                throw new Exception("Header ID " + headerId + " is already in use for type: " + _headerTypeMap[headerId].Name);

            if (_typeHeaderMap.ContainsKey(type))
                throw new Exception("Type " + type.Name + " is already assigned to ID: " + _typeHeaderMap[type]);

            _headerTypeMap[headerId] = type;
            _typeHeaderMap[type] = headerId;

            Type parent = type.BaseType;
            while (parent != null) {
                TypeCache cache;
                if (_typeCacheFactory.PoolData.TryGetValue(parent, out cache)) {
                    cache.HasChildTypes = true;
                    _typeCacheFactory.PoolData[parent] = cache;
                    break;
                }
                parent = parent.BaseType;
            }

            _typeCacheFactory.GetOrBuildInstance(type);
        }

        Type HeaderToType(int header) {
            if (header > 0xFFFF)
                throw new Exception("Header out of bounds");
            return _headerTypeMap.GetValueSafe(header);
        }

        public ushort HeaderForType(Type type, bool isGeneric) {
            int header = 0;
            if (type.IsGenericType)
                type = type.GetGenericTypeDefinition();
            if (_typeHeaderMap.TryGetValue(type, out header)) {
                return (ushort)header;
            }
            throw new Exception("No header for type: " + type.Name);
        }
    }

    public struct KeyValuePairWritable<TKey, TValue> {
        public TKey Key { get; set; }
        public TValue Value { get; set; }
    }

    public struct MemberInfoWithMeta {
        public MemberInfo Info;
        public bool SkipType;
        public bool SkipIsNull;
    }
}