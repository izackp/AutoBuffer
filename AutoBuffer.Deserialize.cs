using CSharp_Library.Utility;
using CSharp_Library.Utility.Data;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace AutoBuffer {
    public partial class Serializer {

        public object ToObject(Type type, byte[] bytes) {
            if (bytes == null)
                throw new ArgumentNullException("bytes");

            DataReader reader = new DataReader(bytes);
            return Deserialize(type, reader);
        }

        public object ToObject(byte[] bytes) {
            if (bytes == null)
                throw new ArgumentNullException("bytes");

            DataReader reader = new DataReader(bytes);
            Type type = ReadType(reader, true, true);
            return Deserialize(type, reader, true, true);
        }

        public T ToObject<T>(byte[] bytes) {
            return (T)ToObject(typeof(T), bytes);
        }

        public object ToObject(ushort header, byte[] bytes) {
            Type type = HeaderToType(header);
            return ToObject(type, bytes);
        }

        ///
        Type ReadType(DataReader reader, bool isGeneric, bool ReadFullHeader = false) {
            int header = ReadFullHeader ? reader.ReadUShort() : (int)reader.ReadVarNum();
            Type baseType = HeaderToType(header);
            if (isGeneric == false)
                return baseType;

            int numGenericArguments = baseType.GetGenericArguments().Length;
            if (numGenericArguments == 0)
                return baseType;

            Type[] subTypes = new Type[numGenericArguments];
            for (int i = 0; i < numGenericArguments; i += 1) {
                int subHeader = (int)reader.ReadVarNum();
                subTypes[i] = HeaderToType(subHeader);
            }

            return baseType.MakeGenericType(subTypes);
        }

        object Deserialize(Type type, DataReader reader, bool skipMetaData = false, bool skipNullByte = false) {
            return Deserialize(type, reader, GetTypeCache(type), skipMetaData, skipNullByte);
        }

        object Deserialize(Type type, DataReader reader, TypeCache typeCache, bool skipMetaData = false, bool skipNullByte = false) {

            if (typeCache.HasChildTypes && skipMetaData == false) {
                type = ReadType(reader, typeCache.IsGeneric);
                if (type == null)
                    return null;
                Type resultType = null;
                if (TypeMapper.TryGetValue(type, out resultType))
                    type = resultType;
            } else if (skipNullByte == false && typeCache.CanBeNull()) {
                bool isNull = reader.ReadBoolean();
                if (isNull)
                    return null;
            }

            if (type == null)
                return null;

            if (typeCache.IsNullable) {
                Type[] types = Reflection.GetGenericArgumentsExt(type);
                return Deserialize(types[0], reader);
            }

            if (type.IsPrimitive)
                return DeserializePrimitive(type, reader);

            if (type == typeof(String))
                return reader.ReadUTF();

            if (type == typeof(Guid))
                return new Guid(reader.ReadBytes(16)); //Automatically in BigEndianFormat

            if (type.IsEnum) {
                Type subType = Enum.GetUnderlyingType(type);
                object value = DeserializePrimitive(subType, reader);
                return Enum.ToObject(type, value);
            }

            if (typeCache.IsList) {
                if (type == typeof(object))
                    return DeserializeArray(typeof(object), reader);

                if (type.IsArray)
                    return DeserializeArray(type.GetElementType(), reader);

                return DeserializeList(type, reader);
            }

            object o = Reflection.CreateInstance(type);

            if (o is IDictionary && typeCache.IsGeneric) {

                Type[] genericArguments = Reflection.GetGenericArgumentsExt(type);
                DeserializeDictionary(genericArguments[0], genericArguments[1], (IDictionary)o, reader);
            } else {
                DeserializeObject(type, o, reader);
            }

            return o;
        }

        object DeserializePrimitive(Type type, DataReader reader) {

            if (type == typeof(Int16))
                return (Int16)reader.ReadVarShort();

            if (type == typeof(Int32))
                return (Int32)reader.ReadVarInt();

            if (type == typeof(Int64))
                return (Int64)reader.ReadVarNum();

            if (type == typeof(UInt16))
                return reader.ReadVarShort();

            if (type == typeof(UInt32))
                return reader.ReadVarInt();

            if (type == typeof(UInt64))
                return reader.ReadVarNum();
            
            if (type == typeof(float))
                return (float)reader.ReadVarInt();

            if (type == typeof(Double))
                return (Double)reader.ReadVarNum();

            if (type == typeof(char))
                return reader.ReadChar();

            if (type == typeof(byte))
                return reader.ReadByte();

            if (type == typeof(sbyte))
                return reader.ReadSByte();

            if (type == typeof(Boolean))
                return reader.ReadBoolean();

            if (type == typeof(Decimal))
                return reader.ReadDecimal();

            if (type == typeof(DateTime)) {
                long seconds = (long)reader.ReadVarNum();
                int nanos = (int)reader.ReadVarInt();

                if (!Timestamp.IsNormalized(seconds, nanos))
                    throw new InvalidOperationException(string.Format(@"Timestamp contains invalid values: Seconds={0}; Nanos={1}", seconds, nanos));

                return Timestamp.UnixEpoch.AddSeconds(seconds).AddTicks(nanos / Duration.NanosecondsPerTick);
            }

            if (type == typeof(TimeSpan)) {
                long seconds = (long)reader.ReadVarNum();
                int nanos = (int)reader.ReadVarInt();

                if (!Duration.IsNormalized(seconds, nanos))
                    throw new InvalidOperationException("Duration was not a valid normalized duration");

                long ticks = seconds * TimeSpan.TicksPerSecond + nanos / Duration.NanosecondsPerTick;
                return TimeSpan.FromTicks(ticks);
            }

            return reader.ReadVarNum();
        }

        Array DeserializeArray(Type type, DataReader reader) {
            int length = (int)reader.ReadVarNum();
            Array arr = Array.CreateInstance(type, length);

            for (int i = 0; i < length; i += 1) {
                object value = Deserialize(type, reader);
                arr.SetValue(value, i);
            }

            return arr;
        }

        IEnumerable DeserializeList(Type type, DataReader reader) {
            var itemType = Reflection.GetListItemType(type);
            var enumerable = (IEnumerable)Reflection.CreateInstance(type);
            IList list = enumerable as IList;
            int length = (int)reader.ReadVarNum();

            Func<object, int> onItem;

            if (list == null) {
#if NET35
            var addMethod = type.GetMethod("Add");
#else
                var addMethod = type.GetRuntimeMethod("Add", new Type[1] { itemType });
#endif
                onItem = (object obj) => {
                    addMethod.Invoke(enumerable, new[] { obj });
                    return 0;
                };
            } else
                onItem = list.Add;

            for (int i = 0; i < length; i += 1) {
                object value = Deserialize(itemType, reader);
                onItem(value);
            }
            return enumerable;
        }

        void DeserializeDictionary(Type K, Type T, IDictionary dict, DataReader reader) {
            var kvpType = typeof(KeyValuePairWritable<,>);
            Type KeyValueType = kvpType.MakeGenericType(K, T);
            Type listType = typeof(List<>);
            Type listGeneric = listType.MakeGenericType(KeyValueType);

            IEnumerable list = DeserializeList(listGeneric, reader);
            EntityMapper mapper = GetEntityMapper(KeyValueType);

            List<MemberMapper> members = mapper.Members;
            foreach (object kvp in list) {
                object key = members[0].Getter(kvp);
                object value = members[1].Getter(kvp);
                dict.Add(key, value);
            }
        }

        void DeserializeObject(Type type, object obj, DataReader reader) {
            EntityMapper entity = GetEntityMapper(type);

            if (entity.CustomSerializers()) {
                entity.Deserializer.Invoke(obj, new object[] { this, reader });
                return;
            }

            foreach (MemberMapper member in entity.Members) {
                object value = Deserialize(member.DataType, reader, member.Cache, member.SkipType, member.SkipIsNull);
                member.Setter(obj, value);
            }
        }
    }
}
